#!/usr/bin/env python3
"""Static consistency checks only. This does not compile or execute C#/WPF."""
from pathlib import Path
from xml.etree import ElementTree as ET
import hashlib
import re
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
for key in set(re.findall(r'"((?:Column_|Group_|Theme_|Signature_|Row_|Error_)[A-Za-z0-9_]+)"', sources)):
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
declared.update(['Text', 'AreRowDetailsFrozen', 'HeadersVisibility'])  # WPF RelativeSource bindings.
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
check('AlternationCount="2"' in xaml and 'AlternatingRowBackground="{DynamicResource AlternateRowBrush}"' in xaml,
      'Alternating rows missing.')

# Both palettes must supply every dynamic brush used by our views/styles.
theme_dir = ROOT / 'src/FileScope.App/Styles/Themes'
palettes = {}
for name in ['Light', 'Dark']:
    palettes[name] = {node.attrib['{http://schemas.microsoft.com/winfx/2006/xaml}Key']: node.attrib['Color']
                      for node in ET.parse(theme_dir / (name + '.xaml')).getroot()}
check(set(palettes['Light']) == set(palettes['Dark']), 'Light/dark palette keys differ.')
for key in set(re.findall(r'\{DynamicResource\s+(\w+)\}', sources)):
    check(key in palettes['Light'], f'Missing dynamic theme brush: {key}')

def luminance(color):
    channels = [int(color[i:i+2], 16) / 255 for i in (1, 3, 5)]
    linear = [value / 12.92 if value <= .04045 else ((value + .055) / 1.055) ** 2.4 for value in channels]
    return sum(value * weight for value, weight in zip(linear, [.2126, .7152, .0722]))

for name, brushes in palettes.items():
    check(brushes['RowBrush'] != brushes['AlternateRowBrush'], f'Identical row colors in {name}.')
    pairs = [('TextBrush', key) for key in ['WindowBrush', 'SurfaceBrush', 'RowBrush', 'AlternateRowBrush', 'RowHoverBrush']]
    pairs += [('MutedBrush', 'WindowBrush'), ('MutedBrush', 'SurfaceBrush'), ('SelectionTextBrush', 'SelectionBrush'),
              ('HeaderTextBrush', 'HeaderBrush'), ('OnAccentBrush', 'AccentBrush'), ('ErrorBrush', 'SurfaceBrush')]
    pairs += [('StatusTextBrush', key) for key in brushes if key.startswith('Status') and key != 'StatusTextBrush']
    for foreground, background in pairs:
        light, dark = sorted([luminance(brushes[foreground]), luminance(brushes[background])], reverse=True)
        check((light + .05) / (dark + .05) >= 4.5, f'Low text contrast in {name}: {foreground}/{background}')

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
