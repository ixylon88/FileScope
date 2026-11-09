#!/usr/bin/env python3
"""Static consistency checks only. This does not compile or execute C#/WPF."""
from pathlib import Path
from xml.etree import ElementTree as ET
import hashlib
import json
import re
import struct
import sys

ROOT = Path(__file__).resolve().parents[1]
failures = []
checks = 0


def check(condition, message):
    global checks
    checks += 1
    if not condition:
        failures.append(message)


files = sorted(path for path in ROOT.rglob('*') if path.is_file()
               and not path.name.endswith('_wpftmp.csproj')
               and not {'bin', 'obj', 'artifacts'}.intersection(path.relative_to(ROOT).parts))
for path in files:
    if path.suffix in {'.xaml', '.resx', '.csproj', '.props', '.manifest'}:
        try:
            ET.parse(path)
            check(True, str(path))
        except ET.ParseError as error:
            check(False, f'{path.relative_to(ROOT)}: {error}')

projects = [p for p in files if p.suffix == '.csproj']
check(len(projects) == 2, 'Exactly two permanent projects are required.')
solution = (ROOT / 'FileScope.sln').read_text(encoding='utf-8-sig')
project_entries = re.findall(r'Project\([^\n]+?= "[^"]+", "([^"]+\.csproj)"', solution)
check(len(project_entries) == 2, 'The solution must contain exactly two C# projects.')
for relative in project_entries:
    check((ROOT / relative.replace('\\', '/')).is_file(), f'Missing solution project: {relative}')
for project in projects:
    parsed = ET.parse(project).getroot()
    check(not parsed.findall('.//PackageReference'), f'Unexpected external package in {project.name}')
    for item in parsed.findall('.//ProjectReference'):
        check((project.parent / item.attrib['Include'].replace('\\', '/')).is_file(), 'Missing project reference')

resource_files = sorted((ROOT / 'src/FileScope.App/Resources').glob('Strings*.resx'))
resources = []
for path in resource_files:
    values = {node.attrib['name']: node.findtext('value') for node in ET.parse(path).getroot().findall('data')}
    check(len(values) == len(ET.parse(path).getroot().findall('data')), f'Duplicate resource key: {path.name}')
    check(all(values.values()), f'Empty resource value: {path.name}')
    resources.append((path.name, values))
check(len(resources) == 3, 'Three language resource files are required.')
base = dict(resources)['Strings.resx']
for name, values in resources:
    check(set(values) == set(base), f'Resource keys differ in {name}')
    for key, text in values.items():
        check(sorted(re.findall(r'\{\d+[^}]*\}', text)) == sorted(re.findall(r'\{\d+[^}]*\}', base[key])),
              f'Format placeholders differ: {name}/{key}')

sources = '\n'.join(p.read_text(encoding='utf-8-sig') for p in files if p.suffix in {'.cs', '.xaml'})
for key in set(re.findall(r'\{loc:Loc\s+([A-Za-z0-9_]+)', sources)):
    check(key in base, f'Unknown XAML resource key: {key}')
for key in set(re.findall(r'"((?:Column_|Group_|HashFormat_|Signature_|Row_|Error_)[A-Za-z0-9_]+)"', sources)):
    check(key in base, f'Unknown C# resource key: {key}')
for key in re.findall(r'SetStatus\("([A-Za-z_]+)"\)', sources):
    check(key in base, f'Unknown status resource key: {key}')
for status in ['Queued', 'Running', 'Completed', 'Failed', 'Cancelled']:
    check('Row_' + status in base, f'Missing dynamic row resource: {status}')
for status in ['NotChecked', 'ValidOnline', 'ValidOffline', 'NoEmbeddedSignature', 'InvalidSignature',
               'Untrusted', 'Expired', 'Revoked', 'RevocationUnknown', 'UnsupportedFormat',
               'UnsupportedPlatform', 'CheckFailed']:
    check('Signature_' + status in base, f'Missing dynamic signature resource: {status}')
for error in ['InvalidPath', 'DirectoryNotSupported', 'NotFound', 'AccessDenied', 'ReadFailure', 'AnalysisFailure']:
    check('Error_' + error in base, f'Missing dynamic error resource: {error}')

# Catch missing simple binding properties. Nested paths are checked at their root;
# actual WPF binding/type validation still requires Verify.ps1 on Windows.
declared = set(re.findall(r'public\s+(?:[\w<>?\[\],.]+\s+)+([A-Za-z_]\w*)\s*(?:\{|=>)', sources))
declared.update(['Text', 'AreRowDetailsFrozen', 'HeadersVisibility',
                 'Padding', 'FontSize', 'Foreground', 'PlacementTarget'])  # Inherited WPF properties in view bindings.
for binding in re.findall(r'\{Binding\s+([A-Za-z_]\w*(?:\.\w+)*)', sources):
    check(binding.split('.')[0] in declared, f'Unknown simple binding root: {binding}')

# Delimiter sanity check, not a C# parser: strip ordinary strings and comments.
token = re.compile(r'//[^\n]*|/\*.*?\*/|@"(?:""|[^"])*"|\$?"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'', re.S)
for path in [p for p in files if p.suffix == '.cs']:
    code = token.sub('', path.read_text(encoding='utf-8-sig'))
    stack = []
    valid = True
    for char in code:
        if char in '({[':
            stack.append(char)
        elif char in ')}]':
            if not stack or stack.pop() != {')': '(', '}': '{', ']': '['}[char]:
                valid = False
                break
    check(valid and not stack, f'Unbalanced C# delimiters: {path.relative_to(ROOT)}')

core = '\n'.join(p.read_text(encoding='utf-8-sig') for p in (ROOT / 'src/FileScope.Core').rglob('*.cs'))
check('System.Windows' not in core and 'FileScope.App' not in core, 'Core must not depend on WPF or App.')
check('NotImplementedException' not in sources and 'TODO' not in sources, 'Unimplemented code found.')
xaml = (ROOT / 'src/FileScope.App/MainWindow.xaml').read_text(encoding='utf-8')
check('AlternationCount="0"' in xaml and 'AlternatingRowBackground=' not in xaml,
      'Row striping must remain disabled.')
options_xaml = ET.parse(ROOT / 'src/FileScope.App/OptionsWindow.xaml').getroot()
xaml_name = '{http://schemas.microsoft.com/winfx/2006/xaml}Name'
theme_selectors = [node for node in options_xaml.iter() if node.attrib.get(xaml_name) == 'ThemeSelector']
check(len(theme_selectors) == 1 and theme_selectors[0].attrib.get('ItemsSource') == '{Binding Themes}'
      and theme_selectors[0].attrib.get('SelectedItem') == '{Binding SelectedTheme}',
      'The theme selector must bind to the options view model.')
palettes = [ET.parse(ROOT / f'src/FileScope.App/Styles/Themes/{theme}ThemePalette.xaml').getroot()
            for theme in ['Light', 'Dark']]
palette_keys = [{node.attrib['{http://schemas.microsoft.com/winfx/2006/xaml}Key'] for node in palette}
                for palette in palettes]
check(palette_keys[0] == palette_keys[1], 'Both palettes must expose the same resource keys.')
dynamic_keys = set(re.findall(r'\{DynamicResource ([A-Za-z_]\w*)\}', sources))
# These keys belong to WPF's native Fluent dictionary, not our palettes.
fluent_keys = {'SystemFillColorAttentionBrush', 'SystemFillColorCriticalBrush', 'TextOnAccentFillColorSelectedTextBrush'}
check(dynamic_keys - fluent_keys <= palette_keys[0], 'Every custom dynamic resource must exist in both palettes.')
theme_contract = (ROOT / 'src/FileScope.App/Services/IThemeService.cs').read_text(encoding='utf-8')
verify_script = (ROOT / 'Verify.ps1').read_text(encoding='utf-8-sig')
check('System.Windows' not in theme_contract and "'Services/IThemeService.cs'" in verify_script
      and "'Services/ThemeService.cs'" not in verify_script,
      'Presentation checks must compile the theme contract without the WPF implementation.')
controls = (ROOT / 'src/FileScope.App/Styles/Controls.xaml').read_text(encoding='utf-8')
check('ControlTemplate' not in controls, 'Use native control templates for the application theme.')
app_theme = ET.parse(ROOT / 'src/FileScope.App/App.xaml').getroot().attrib.get('ThemeMode')
ui_checks = (ROOT / 'tests/UiChecks.cs').read_text(encoding='utf-8')
check(app_theme is not None and f'ThemeMode = ThemeMode.{app_theme}' in ui_checks,
      'The UI test host must use the same ThemeMode as App.xaml.')
namespace = '{http://schemas.microsoft.com/winfx/2006/xaml/presentation}'
behavior = '{clr-namespace:FileScope.App.Behaviors}ColumnLayoutBehavior.Id'
view = ET.fromstring(xaml)
columns = [node for node in view.iter() if node.tag in {namespace + 'DataGridTextColumn', namespace + 'DataGridTemplateColumn'}]
ids = [column.attrib.get(behavior) for column in columns]
check(len(ids) == 39 and None not in ids and len(set(ids)) == len(ids), 'Missing or duplicate column IDs.')
catalog = (ROOT / 'src/FileScope.App/ViewModels/ColumnOption.cs').read_text(encoding='utf-8')
catalog_ids = re.findall(r'new\("(\w+)",', catalog)
check(set(ids) == set(catalog_ids), 'Options must include every table column, including hidden columns.')
for column in columns:
    check(column.attrib.get('Width') == 'Auto', 'Column must use content-driven sizing.')
    check(0 < float(column.attrib['MinWidth']) <= float(column.attrib['MaxWidth']) < 1000,
          'Invalid column width bounds: ' + column.attrib[behavior])
for format in ['HexUpper', 'HexLower', 'HexColon', 'Base64', 'Base64Url']:
    check('HashFormat_' + format in base, 'Missing hash format label: ' + format)

# Workspace tasks must resolve against the named folder and run the checked-in scripts.
workspace = json.loads((ROOT / 'FileScope.code-workspace').read_text(encoding='utf-8-sig'))
check(any(folder.get('name') == 'FileScope' and folder.get('path') == '.' for folder in workspace['folders']),
      'Task workspace folder must resolve to the repository root.')
tasks = workspace['tasks']['tasks']
check(len(tasks) == 6 and len({task['label'] for task in tasks}) == 6, 'Expected six distinct workspace tasks.')
for task in tasks:
    check(task['type'] == 'process' and task['command'] == 'powershell.exe', 'Use PowerShell process tasks.')
    args = task['args']
    script = args[args.index('-File') + 1]
    check(script.startswith('${workspaceFolder:FileScope}/') and (ROOT / script.split('/')[-1]).is_file(),
          'Task must invoke an existing script in this workspace: ' + task['label'])
    check(task['options']['cwd'] == '${workspaceFolder:FileScope}', 'Task working directory must be the repository root.')
publish = [task for task in tasks if '-Publish' in task['args']]
check(len(publish) == 4 and {(task['args'][task['args'].index('-Runtime') + 1], '-SelfContained' in task['args'])
                           for task in publish} == {('win-x64', False), ('win-x64', True), ('win-arm64', False), ('win-arm64', True)},
      'Publish tasks must cover both runtimes with and without self-contained deployment.')
check(any(task['args'][-1].endswith('/Verify.ps1') for task in tasks), 'Verify task must invoke Verify.ps1.')

# The generated WPF entry point needs an embedded splash, not a loose output file.
app_project = ET.parse(ROOT / 'src/FileScope.App/FileScope.App.csproj').getroot()
splashes = app_project.findall('.//SplashScreen')
check(len(splashes) == 1 and splashes[0].get('Include') == r'Resources\FileScopeSplash.png',
      'Embed the small startup image with WPF SplashScreen build action.')
splash = ROOT / 'src/FileScope.App/Resources/FileScopeSplash.png'
data = splash.read_bytes() if splash.is_file() else b''
check(data.startswith(b'\x89PNG\r\n\x1a\n') and len(data) >= 24 and struct.unpack('>II', data[16:24]) == (256, 256),
      'The splash must remain a small 256px PNG rather than the large source image.')

# Independently check the known-answer constants shipped with the C# harness.
tests = (ROOT / 'tests/CoreChecks.cs').read_text(encoding='utf-8')
vectors = re.findall(r'\[ChecksumAlgorithm\.(\w+)\] = "([A-F0-9]+)"', tests)
check(len(vectors) == 5, 'Five abc known-answer vectors are required.')
for name, expected in vectors:
    check(hashlib.new(name.lower(), b'abc').hexdigest().upper() == expected, f'Incorrect known-answer vector: {name}')

print(f'Static checks: {checks}; files inspected: {len(files)}; resource keys per language: {len(base)}.')
if failures:
    for failure in failures:
        print('FAIL:', failure)
    sys.exit(1)
print('PASS: structure, XML, resources, binding roots, delimiter sanity and test-vector constants.')
print('NOT EXECUTED BY THIS SCRIPT: .NET compilation, C# tests, WPF runtime, native Authenticode, visual layout.')
