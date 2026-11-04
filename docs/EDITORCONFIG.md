# EditorConfig: Einstellungen und Wirkung

Die Werte entsprechen der sortierten `.editorconfig`. Innerhalb jeder Gruppe sind die Schlüssel alphabetisch bzw. nach Regel-ID geordnet. Kommentare stehen in der Konfigurationsdatei unmittelbar darüber; diese Tabelle zeigt sie daneben.

**Stufen:** `none` = ausgeschaltet, `suggestion` = Hinweis, `warning` = Warnung, `error` = Fehler. Die allgemeine Analyzer-Stufe betrifft standardmäßig aktive Regeln; explizite Einzelregeln haben Vorrang. Welche IDE-Stilregeln im Build laufen, hängt zusätzlich von der Projekt-/SDK-Konfiguration ab.

**Bereinigung:** Die identische PascalCase-Definition ist nur noch einmal vorhanden. `csharp_style_pattern_local_over_anonymous_function` war ein unbekannter Schlüssel und heißt jetzt korrekt `csharp_style_prefer_local_over_anonymous_function`. Der gewünschte Wert `false:suggestion` bleibt erhalten. Alle übrigen wirksamen Werte bleiben gleich.

**Zusätzliche Analyzer:** StyleCop (`SA`/`SX`), AsyncFixer und VSTHRD sind in den Projektdateien nicht als Pakete eingebunden. Ihre Einträge bleiben als vorbereitete Regeln erhalten; im Projektbuild wirken sie erst, wenn die entsprechenden Analyzer verfügbar sind.

## 01 | Vererbung

Gültigkeitsbereich: `Datei / Vererbung`.

| Einstellung | Wert | Wirkung |
| --- | --- | --- |
| `root` | `true` | Suche nach weiteren .editorconfig-Dateien oberhalb dieses Ordners beenden. |

## 02 | Allgemeines Dateiformat

Gültigkeitsbereich: `[*]`.

| Einstellung | Wert | Wirkung |
| --- | --- | --- |
| `charset` | `utf-8` | Als UTF-8 ohne BOM speichern. |
| `end_of_line` | `crlf` | Windows-Zeilenenden (CRLF) verwenden. |
| `indent_size` | `4` | Anzahl der Leerzeichen pro Einrückungsebene. |
| `indent_style` | `space` | Mit Leerzeichen statt Tabulatoren einrücken. |
| `insert_final_newline` | `true` | Abschließenden Zeilenumbruch beim Speichern ergänzen (true) bzw. nicht erzwingen (false). |

## 03 | XAML, Projekte und Datenformate

Gültigkeitsbereich: `[*.{xaml,csproj,props,resx,json}]`.

| Einstellung | Wert | Wirkung |
| --- | --- | --- |
| `indent_size` | `2` | Anzahl der Leerzeichen pro Einrückungsebene. |

## 04 | C# / VB: Dateiformat

Gültigkeitsbereich: `[*.{cs,vb}]`.

Für C# / VB bleibt insert_final_newline = false als bewusste Ausnahme erhalten.

| Einstellung | Wert | Wirkung |
| --- | --- | --- |
| `indent_size` | `4` | Anzahl der Leerzeichen pro Einrückungsebene. |
| `indent_style` | `space` | Mit Leerzeichen statt Tabulatoren einrücken. |
| `insert_final_newline` | `false` | Abschließenden Zeilenumbruch beim Speichern ergänzen (true) bzw. nicht erzwingen (false). |
| `trim_trailing_whitespace` | `true` | Leerzeichen am Zeilenende beim Speichern entfernen. |

## 05 | C# / .NET: Schreibweise

Gültigkeitsbereich: `[*.{cs,vb}]`.

Gilt weiter für [*.{cs,vb}]. C#-spezifische Optionen werden bei VB ignoriert.
IDE0007/IDE0008 sind unten ausgeschaltet: Die var-Präferenzen erzeugen daher keine entsprechenden Diagnosehinweise.

| Einstellung | Wert | Wirkung |
| --- | --- | --- |
| `csharp_prefer_braces` | `when_multiline:error` | Bei mehrzeiligen Anweisungen geschweifte Klammern verlangen. |
| `csharp_prefer_simple_default_expression` | `true:suggestion` | default statt default(Typ) bevorzugen, wenn der Typ ableitbar ist. |
| `csharp_preferred_modifier_order` | `public, private, protected, internal, static, extern, new, virtual, abstract, sealed, override, readonly, unsafe, volatile, async:suggestion` | Modifikatoren wie public/static/readonly in dieser Reihenfolge anordnen. |
| `csharp_style_expression_bodied_accessors` | `when_on_single_line:suggestion` | Für get/set => bevorzugen, wenn der Ausdruck einzeilig ist. |
| `csharp_style_expression_bodied_constructors` | `false:suggestion` | Konstruktoren mit Blockkörper statt => bevorzugen. |
| `csharp_style_expression_bodied_local_functions` | `when_on_single_line:suggestion` | Für lokale Funktionen => bevorzugen, wenn der Ausdruck einzeilig ist. |
| `csharp_style_expression_bodied_methods` | `false:suggestion` | Methoden mit Blockkörper statt => bevorzugen. |
| `csharp_style_expression_bodied_properties` | `when_on_single_line:suggestion` | Für Eigenschaften => bevorzugen, wenn der Ausdruck einzeilig ist. |
| `csharp_style_inlined_variable_declaration` | `true:suggestion` | Variablen direkt im out-Argument deklarieren. |
| `csharp_style_pattern_matching_over_as_with_null_check` | `true:suggestion` | Typmuster statt as mit anschließendem null-Test bevorzugen. |
| `csharp_style_prefer_local_over_anonymous_function` | `false:suggestion` | Lambda-Ausdrücke statt lokaler Funktionen bevorzugen (IDE0039). |
| `csharp_style_var_elsewhere` | `false:suggestion` | In den übrigen Fällen den Typ explizit ausschreiben. |
| `csharp_style_var_for_built_in_types` | `false:suggestion` | Bei eingebauten Typen int/string usw. statt var bevorzugen. |
| `csharp_style_var_when_type_is_apparent` | `true:suggestion` | var bevorzugen, wenn der Typ rechts bereits erkennbar ist. |
| `dotnet_style_explicit_tuple_names` | `false:suggestion` | Explizite Tupel-Elementnamen beim Zugriff nicht bevorzugen. |
| `dotnet_style_object_initializer` | `true:suggestion` | Objektinitialisierer statt nachfolgender Zuweisungen bevorzugen. |
| `dotnet_style_operator_placement_when_wrapping` | `beginning_of_line` | Bei Zeilenumbrüchen den Operator an den Anfang der Folgezeile setzen. |
| `dotnet_style_parentheses_in_arithmetic_binary_operators` | `never_if_unnecessary:none` | Unnötige Klammern in arithmetischen Ausdrücken weglassen. |
| `dotnet_style_parentheses_in_other_binary_operators` | `never_if_unnecessary:none` | Unnötige Klammern bei anderen binären Operatoren weglassen. |
| `dotnet_style_parentheses_in_relational_binary_operators` | `never_if_unnecessary:none` | Unnötige Klammern in Vergleichsausdrücken weglassen. |
| `dotnet_style_predefined_type_for_locals_parameters_members` | `true:suggestion` | Sprachalias wie int/string bei Deklarationen bevorzugen. |
| `dotnet_style_predefined_type_for_member_access` | `true:suggestion` | Sprachalias wie int.MaxValue statt Int32.MaxValue bevorzugen. |
| `dotnet_style_prefer_inferred_tuple_names` | `true:suggestion` | Ableitbare Tupel-Elementnamen nicht erneut ausschreiben. |
| `dotnet_style_qualification_for_event` | `false:suggestion` | Bei Ereigniszugriffen this./Me. weglassen. |
| `dotnet_style_qualification_for_field` | `false:suggestion` | Bei Feldzugriffen this./Me. weglassen. |
| `dotnet_style_qualification_for_method` | `false:suggestion` | Bei Methodenaufrufen this./Me. weglassen. |
| `dotnet_style_qualification_for_property` | `false:suggestion` | Bei Eigenschaftszugriffen this./Me. weglassen. |
| `dotnet_style_require_accessibility_modifiers` | `for_non_interface_members:suggestion` | Sichtbarkeit explizit angeben, außer für Interface-Member. |

## 06 | C#: Einrückung, Klammern und Abstände

Gültigkeitsbereich: `[*.{cs,vb}]`.

| Einstellung | Wert | Wirkung |
| --- | --- | --- |
| `csharp_indent_case_contents` | `true` | Anweisungen innerhalb eines case einrücken. |
| `csharp_indent_case_contents_when_block` | `false` | Zusätzliche Einrückung des Blockkörpers eines case nicht vorsehen. |
| `csharp_indent_labels` | `one_less_than_current` | Sprungmarken eine Ebene weniger als den umgebenden Code einrücken. |
| `csharp_indent_switch_labels` | `true` | case/default gegenüber switch einrücken. |
| `csharp_new_line_before_else` | `true` | else auf eine neue Zeile setzen. |
| `csharp_new_line_before_members_in_object_initializers` | `true` | Zuweisungen in Objektinitialisierern auf getrennte Zeilen setzen. |
| `csharp_new_line_before_open_brace` | `all` | Öffnende Klammern für alle aufgeführten Konstrukte auf eine neue Zeile setzen. |
| `csharp_preserve_single_line_blocks` | `true` | Bereits einzeilige Blöcke beim Formatieren einzeilig lassen. |
| `csharp_space_after_cast` | `false` | Kein Leerzeichen zwischen Cast und Ausdruck: (int)value. |
| `csharp_space_after_colon_in_inheritance_clause` | `true` | Leerzeichen nach dem Doppelpunkt einer Vererbungsliste setzen. |
| `csharp_space_after_keywords_in_control_flow_statements` | `true` | Leerzeichen nach if/for/while usw. setzen: if (…). |
| `csharp_space_before_colon_in_inheritance_clause` | `true` | Leerzeichen vor dem Doppelpunkt einer Vererbungsliste setzen. |
| `csharp_space_between_method_call_empty_parameter_list_parentheses` | `false` | Leere Argumentklammern von Aufrufen ohne Leerzeichen schreiben. |
| `csharp_space_between_method_call_name_and_opening_parenthesis` | `false` | Kein Leerzeichen zwischen Methodenname und Aufrufklammer. |
| `csharp_space_between_method_call_parameter_list_parentheses` | `false` | Keine inneren Leerzeichen in Argumentklammern von Aufrufen. |
| `csharp_space_between_method_declaration_empty_parameter_list_parentheses` | `false` | Leere Parameterklammern von Deklarationen ohne Leerzeichen schreiben. |
| `csharp_space_between_method_declaration_parameter_list_parentheses` | `false` | Keine inneren Leerzeichen in Parameterklammern von Deklarationen. |
| `dotnet_sort_system_directives_first` | `true` | System-using-Anweisungen vor die anderen using-Anweisungen sortieren. |

## 07 | Analyzer: Grundstufe

Gültigkeitsbereich: `[*.{cs,vb}]`.

| Einstellung | Wert | Wirkung |
| --- | --- | --- |
| `dotnet_analyzer_diagnostic.severity` | `error` | Standardmäßig aktive Analyzer-Regeln als Fehler behandeln; Einzelregeln haben Vorrang. |

## 08 | .NET-Qualitätsregeln (CA)

Gültigkeitsbereich: `[*.{cs,vb}]`.

| Einstellung | Wert | Wirkung |
| --- | --- | --- |
| `dotnet_diagnostic.CA1002.severity` | `suggestion` | [HINWEIS] CA1002: Öffentliche List<T>-APIs vermeiden. |
| `dotnet_diagnostic.CA1005.severity` | `suggestion` | [HINWEIS] CA1005: Zu viele generische Typparameter vermeiden. |
| `dotnet_diagnostic.CA1024.severity` | `none` | [AUS] CA1024: Geeignete Methoden durch Eigenschaften ersetzen. |
| `dotnet_diagnostic.CA1031.severity` | `suggestion` | [HINWEIS] CA1031: Pauschales Abfangen allgemeiner Exceptions vermeiden. |
| `dotnet_diagnostic.CA1040.severity` | `suggestion` | [HINWEIS] CA1040: Leere Interfaces vermeiden. |
| `dotnet_diagnostic.CA1062.severity` | `none` | [AUS] CA1062: Argumente öffentlicher Methoden validieren. |
| `dotnet_diagnostic.CA1303.severity` | `none` | [AUS] CA1303: Lokalisierbare Texte aus Ressourcen beziehen. |
| `dotnet_diagnostic.CA1305.severity` | `suggestion` | [HINWEIS] CA1305: Formatierung mit expliziter Kultur ausführen. |
| `dotnet_diagnostic.CA1309.severity` | `suggestion` | [HINWEIS] CA1309: Ordinale Zeichenfolgenvergleiche bevorzugen. |
| `dotnet_diagnostic.CA1501.severity` | `suggestion` | [HINWEIS] CA1501: Übermäßig tiefe Vererbung vermeiden. |
| `dotnet_diagnostic.CA1502.severity` | `suggestion` | [HINWEIS] CA1502: Hohe zyklomatische Komplexität vermeiden. |
| `dotnet_diagnostic.CA1505.severity` | `suggestion` | [HINWEIS] CA1505: Schlecht wartbaren Code erkennen. |
| `dotnet_diagnostic.CA1506.severity` | `suggestion` | [HINWEIS] CA1506: Starke Kopplung zwischen Klassen vermeiden. |
| `dotnet_diagnostic.CA1508.severity` | `none` | [AUS] CA1508: Bedingungen mit konstantem Ergebnis erkennen. |
| `dotnet_diagnostic.CA1710.severity` | `suggestion` | [HINWEIS] CA1710: Vorgeschriebene Namenssuffixe verwenden. |
| `dotnet_diagnostic.CA1716.severity` | `suggestion` | [HINWEIS] CA1716: Sprachschlüsselwörter als Bezeichner vermeiden. |
| `dotnet_diagnostic.CA1724.severity` | `suggestion` | [HINWEIS] CA1724: Typ-/Namespace-Namenskonflikte vermeiden. |
| `dotnet_diagnostic.CA1727.severity` | `none` | [AUS] CA1727: Benannte Logging-Platzhalter in PascalCase schreiben. |
| `dotnet_diagnostic.CA1822.severity` | `suggestion` | [HINWEIS] CA1822: Instanzunabhängige Member static machen. |
| `dotnet_diagnostic.CA1848.severity` | `none` | [AUS] CA1848: Effizientes Logging über LoggerMessage verwenden. |
| `dotnet_diagnostic.CA1852.severity` | `none` | [AUS] CA1852: Nicht abgeleitete interne Klassen versiegeln. |
| `dotnet_diagnostic.CA1853.severity` | `none` | [AUS] CA1853: ContainsKey vor Dictionary.Remove vermeiden. |
| `dotnet_diagnostic.CA1854.severity` | `none` | [AUS] CA1854: TryGetValue statt doppeltem Dictionary-Zugriff verwenden. |
| `dotnet_diagnostic.CA2000.severity` | `suggestion` | [HINWEIS] CA2000: Entsorgbare Objekte rechtzeitig freigeben. |
| `dotnet_diagnostic.CA2002.severity` | `suggestion` | [HINWEIS] CA2002: Sperren auf öffentlich erreichbaren Objekten vermeiden. |
| `dotnet_diagnostic.CA2100.severity` | `suggestion` | [HINWEIS] CA2100: Unsichere SQL-Befehlstexte erkennen. |
| `dotnet_diagnostic.CA2225.severity` | `none` | [AUS] CA2225: Benannte Alternativen für Operatorüberladungen anbieten. |
| `dotnet_diagnostic.CA2254.severity` | `none` | [AUS] CA2254: Konstante Logging-Nachrichtenvorlagen verwenden. |
| `dotnet_diagnostic.CA2300.severity` | `suggestion` | [HINWEIS] CA2300: Unsicheren BinaryFormatter-Einsatz erkennen. |
| `dotnet_diagnostic.CA2301.severity` | `suggestion` | [HINWEIS] CA2301: BinaryFormatter ohne sicheren Binder erkennen. |
| `dotnet_diagnostic.CA5362.severity` | `suggestion` | [HINWEIS] CA5362: Referenzzyklen in deserialisierten Objektgraphen beachten. |
| `dotnet_diagnostic.CA5392.severity` | `suggestion` | [HINWEIS] CA5392: DLL-Suchpfade für native Imports einschränken. |

## 09 | IDE-Stilregeln und Compilerhinweise

Gültigkeitsbereich: `[*.{cs,vb}]`.

| Einstellung | Wert | Wirkung |
| --- | --- | --- |
| `dotnet_diagnostic.CS1591.severity` | `suggestion` | [HINWEIS] CS1591: Fehlende öffentliche XML-Dokumentation melden, wenn XML-Dokumentationsausgabe aktiv ist. |
| `dotnet_diagnostic.IDE0007.severity` | `none` | [AUS] IDE0007: var statt explizitem Typ verwenden. |
| `dotnet_diagnostic.IDE0008.severity` | `none` | [AUS] IDE0008: Expliziten Typ statt var verwenden. |
| `dotnet_diagnostic.IDE0010.severity` | `suggestion` | [HINWEIS] IDE0010: Fehlende switch-Fälle ergänzen. |
| `dotnet_diagnostic.IDE0022.severity` | `none` | [AUS] IDE0022: Methoden mit Ausdruckskörper vorschlagen. |
| `dotnet_diagnostic.IDE0028.severity` | `suggestion` | [HINWEIS] IDE0028: Collection-Initialisierer verwenden. |
| `dotnet_diagnostic.IDE0031.severity` | `none` | [AUS] IDE0031: Nullbedingte Zugriffe mit ?. verwenden. |
| `dotnet_diagnostic.IDE0045.severity` | `none` | [AUS] IDE0045: Zuweisungen mit bedingtem Ausdruck vereinfachen. |
| `dotnet_diagnostic.IDE0046.severity` | `none` | [AUS] IDE0046: return mit bedingtem Ausdruck vereinfachen. |
| `dotnet_diagnostic.IDE0050.severity` | `none` | [AUS] IDE0050: Anonyme Typen in Tupel umwandeln. |
| `dotnet_diagnostic.IDE0055.severity` | `warning` | [WARNUNG] IDE0055: Die konfigurierten Formatierungsregeln prüfen. |
| `dotnet_diagnostic.IDE0057.severity` | `suggestion` | [HINWEIS] IDE0057: Bereichsoperator (..) verwenden. |
| `dotnet_diagnostic.IDE0058.severity` | `none` | [AUS] IDE0058: Unbenutzte Ausdrucksergebnisse explizit behandeln. |
| `dotnet_diagnostic.IDE0070.severity` | `none` | [AUS] IDE0070: System.HashCode für Hashcode-Berechnungen verwenden. |
| `dotnet_diagnostic.IDE0090.severity` | `none` | [AUS] IDE0090: Zieltypisiertes new() verwenden. |
| `dotnet_diagnostic.IDE0130.severity` | `none` | [AUS] IDE0130: Namespace an die Ordnerstruktur anpassen. |
| `dotnet_diagnostic.IDE0160.severity` | `none` | [AUS] IDE0160: Namespace mit Blockklammern verwenden. |
| `dotnet_diagnostic.IDE0250.severity` | `none` | [AUS] IDE0250: Geeignete Structs readonly machen. |
| `dotnet_diagnostic.IDE0270.severity` | `none` | [AUS] IDE0270: null-Prüfungen vereinfachen. |
| `dotnet_diagnostic.IDE0290.severity` | `suggestion` | [HINWEIS] IDE0290: Primärkonstruktoren verwenden. |
| `dotnet_diagnostic.IDE0300.severity` | `suggestion` | [HINWEIS] IDE0300: Collection-Ausdrücke für Arrays verwenden. |
| `dotnet_diagnostic.IDE0301.severity` | `suggestion` | [HINWEIS] IDE0301: Collection-Ausdrücke für leere Collections verwenden. |
| `dotnet_diagnostic.IDE0305.severity` | `suggestion` | [HINWEIS] IDE0305: Fluent-Aufrufketten durch Collection-Ausdrücke ersetzen. |

## 10 | StyleCop (optional)

Gültigkeitsbereich: `[*.{cs,vb}]`.

Nur wirksam mit StyleCop-Analyzern. Im Projekt ist kein solches Paket referenziert.

| Einstellung | Wert | Wirkung |
| --- | --- | --- |
| `dotnet_analyzer_diagnostic.category-StyleCop.CSharp.DocumentationRules.severity` | `none` | Alle standardmäßig aktiven StyleCop-Dokumentationsregeln ausschalten. |
| `dotnet_diagnostic.SA0001.severity` | `none` | [AUS] SA0001: Auf deaktivierte XML-Kommentaranalyse hinweisen. |
| `dotnet_diagnostic.SA1010.severity` | `none` | [AUS] SA1010: Abstände an öffnenden eckigen Klammern prüfen. |
| `dotnet_diagnostic.SA1101.severity` | `none` | [AUS] SA1101: Instanzzugriffe mit this. qualifizieren. |
| `dotnet_diagnostic.SA1124.severity` | `suggestion` | [HINWEIS] SA1124: #region-Blöcke vermeiden. |
| `dotnet_diagnostic.SA1200.severity` | `none` | [AUS] SA1200: Platzierung von using-Anweisungen prüfen. |
| `dotnet_diagnostic.SA1305.severity` | `suggestion` | [HINWEIS] SA1305: Ungarische Typpräfixe in Namen vermeiden. |
| `dotnet_diagnostic.SA1309.severity` | `none` | [AUS] SA1309: Feldnamen ohne führenden Unterstrich verlangen. |
| `dotnet_diagnostic.SA1404.severity` | `suggestion` | [HINWEIS] SA1404: Begründungen für Analyzer-Unterdrückungen verlangen. |
| `dotnet_diagnostic.SA1407.severity` | `none` | [AUS] SA1407: Operatorrangfolge in Rechenausdrücken durch Klammern verdeutlichen. |
| `dotnet_diagnostic.SA1412.severity` | `none` | [AUS] SA1412: UTF-8 mit BOM verlangen. |
| `dotnet_diagnostic.SA1413.severity` | `none` | [AUS] SA1413: Abschließende Kommas in mehrzeiligen Initialisierern verlangen. |
| `dotnet_diagnostic.SA1500.severity` | `none` | [AUS] SA1500: Klammern mehrzeiliger Anweisungen auf eigene Zeilen setzen. |
| `dotnet_diagnostic.SA1503.severity` | `none` | [AUS] SA1503: Geschweifte Klammern um Anweisungsblöcke verlangen. |
| `dotnet_diagnostic.SA1504.severity` | `none` | [AUS] SA1504: Accessor-Körper einheitlich ein- oder mehrzeilig schreiben. |
| `dotnet_diagnostic.SA1513.severity` | `none` | [AUS] SA1513: Leerzeile nach schließenden Blockklammern verlangen. |
| `dotnet_diagnostic.SA1516.severity` | `none` | [AUS] SA1516: Deklarationen durch Leerzeilen trennen. |
| `dotnet_diagnostic.SX1101.severity` | `error` | [FEHLER] SX1101: Instanzzugriffe ohne this. verlangen. |
| `dotnet_diagnostic.SX1309.severity` | `error` | [FEHLER] SX1309: Private Instanzfelder mit Unterstrich beginnen lassen. |
| `dotnet_diagnostic.SX1309S.severity` | `error` | [FEHLER] SX1309S: Private veränderliche statische Felder mit Unterstrich beginnen lassen. |

## 11 | Async-/Threading-Analyzer (optional)

Gültigkeitsbereich: `[*.{cs,vb}]`.

Nur wirksam mit AsyncFixer bzw. Microsoft.VisualStudio.Threading.Analyzers.

| Einstellung | Wert | Wirkung |
| --- | --- | --- |
| `dotnet_diagnostic.AsyncFixer01.severity` | `none` | [AUS] AsyncFixer01: Überflüssiges async/await erkennen. |
| `dotnet_diagnostic.AsyncFixer02.severity` | `none` | [AUS] AsyncFixer02: Blockierende Aufrufe in async-Methoden erkennen. |
| `dotnet_diagnostic.AsyncFixer03.severity` | `none` | [AUS] AsyncFixer03: async void-Methoden und -Delegaten erkennen. |
| `dotnet_diagnostic.VSTHRD001.severity` | `none` | [AUS] VSTHRD001: Veraltete Threadwechsel vermeiden. |
| `dotnet_diagnostic.VSTHRD002.severity` | `none` | [AUS] VSTHRD002: Problematische synchrone Warteaufrufe vermeiden. |
| `dotnet_diagnostic.VSTHRD003.severity` | `none` | [AUS] VSTHRD003: await auf fremd erzeugten Tasks prüfen. |
| `dotnet_diagnostic.VSTHRD103.severity` | `none` | [AUS] VSTHRD103: In async-Methoden asynchrone APIs verwenden. |
| `dotnet_diagnostic.VSTHRD104.severity` | `none` | [AUS] VSTHRD104: Asynchrone API-Alternative anbieten. |
| `dotnet_diagnostic.VSTHRD110.severity` | `none` | [AUS] VSTHRD110: Ergebnisse asynchroner Aufrufe beobachten. |

## 12 | Benennung: Regeln

Gültigkeitsbereich: `[*.{cs,vb}]`.

| Einstellung | Wert | Wirkung |
| --- | --- | --- |
| `dotnet_naming_rule.interface_should_be_begins_with_i.severity` | `suggestion` | Verletzungen dieser Namensregel als Vorschlag anzeigen. |
| `dotnet_naming_rule.interface_should_be_begins_with_i.style` | `begins_with_i` | Für diese Namensregel den Stil "begins_with_i" verwenden. |
| `dotnet_naming_rule.interface_should_be_begins_with_i.symbols` | `interface` | Diese Namensregel auf die Symbolgruppe "interface" anwenden. |
| `dotnet_naming_rule.non_field_members_should_be_pascal_case.severity` | `suggestion` | Verletzungen dieser Namensregel als Vorschlag anzeigen. |
| `dotnet_naming_rule.non_field_members_should_be_pascal_case.style` | `pascal_case` | Für diese Namensregel den Stil "pascal_case" verwenden. |
| `dotnet_naming_rule.non_field_members_should_be_pascal_case.symbols` | `non_field_members` | Diese Namensregel auf die Symbolgruppe "non_field_members" anwenden. |
| `dotnet_naming_rule.types_should_be_pascal_case.severity` | `suggestion` | Verletzungen dieser Namensregel als Vorschlag anzeigen. |
| `dotnet_naming_rule.types_should_be_pascal_case.style` | `pascal_case` | Für diese Namensregel den Stil "pascal_case" verwenden. |
| `dotnet_naming_rule.types_should_be_pascal_case.symbols` | `types` | Diese Namensregel auf die Symbolgruppe "types" anwenden. |

## 13 | Benennung: Symbolgruppen

Gültigkeitsbereich: `[*.{cs,vb}]`.

| Einstellung | Wert | Wirkung |
| --- | --- | --- |
| `dotnet_naming_symbols.interface.applicable_accessibilities` | `public, internal, private, protected, protected_internal, private_protected` | Die Gruppe auf diese Sichtbarkeiten anwenden. |
| `dotnet_naming_symbols.interface.applicable_kinds` | `interface` | Diese Symbolarten in die Gruppe aufnehmen. |
| `dotnet_naming_symbols.interface.required_modifiers` | `` (leer) | Keine zusätzlichen Modifikatoren wie static/readonly voraussetzen. |
| `dotnet_naming_symbols.non_field_members.applicable_accessibilities` | `public, internal, private, protected, protected_internal, private_protected` | Die Gruppe auf diese Sichtbarkeiten anwenden. |
| `dotnet_naming_symbols.non_field_members.applicable_kinds` | `property, event, method` | Diese Symbolarten in die Gruppe aufnehmen. |
| `dotnet_naming_symbols.non_field_members.required_modifiers` | `` (leer) | Keine zusätzlichen Modifikatoren wie static/readonly voraussetzen. |
| `dotnet_naming_symbols.types.applicable_accessibilities` | `public, internal, private, protected, protected_internal, private_protected` | Die Gruppe auf diese Sichtbarkeiten anwenden. |
| `dotnet_naming_symbols.types.applicable_kinds` | `class, struct, interface, enum` | Diese Symbolarten in die Gruppe aufnehmen. |
| `dotnet_naming_symbols.types.required_modifiers` | `` (leer) | Keine zusätzlichen Modifikatoren wie static/readonly voraussetzen. |

## 14 | Benennung: Stile

Gültigkeitsbereich: `[*.{cs,vb}]`.

| Einstellung | Wert | Wirkung |
| --- | --- | --- |
| `dotnet_naming_style.begins_with_i.capitalization` | `pascal_case` | Jeden Wortbestandteil groß beginnen: PascalCase. |
| `dotnet_naming_style.begins_with_i.required_prefix` | `I` | Den Namenspräfix "I" verlangen. |
| `dotnet_naming_style.begins_with_i.required_suffix` | `` (leer) | Keinen Namenssuffix verlangen. |
| `dotnet_naming_style.begins_with_i.word_separator` | `` (leer) | Wortbestandteile ohne Trennzeichen verbinden. |
| `dotnet_naming_style.pascal_case.capitalization` | `pascal_case` | Jeden Wortbestandteil groß beginnen: PascalCase. |
| `dotnet_naming_style.pascal_case.required_prefix` | `` (leer) | Keinen Namenspräfix verlangen. |
| `dotnet_naming_style.pascal_case.required_suffix` | `` (leer) | Keinen Namenssuffix verlangen. |
| `dotnet_naming_style.pascal_case.word_separator` | `` (leer) | Wortbestandteile ohne Trennzeichen verbinden. |

## 15 | Ausnahmen für [**Test**.cs]

Gültigkeitsbereich: `[**Test**.cs]`.

Dieses Muster trifft Namen mit "Test"; CoreChecks.cs/UiChecks.cs verwenden stattdessen tests/.editorconfig.

| Einstellung | Wert | Wirkung |
| --- | --- | --- |
| `dotnet_diagnostic.CA1707.severity` | `none` | [AUS] CA1707: Unterstriche in Bezeichnern vermeiden. |
| `dotnet_diagnostic.CA1822.severity` | `none` | [AUS] CA1822: Instanzunabhängige Member static machen. |
| `dotnet_diagnostic.CA5394.severity` | `none` | [AUS] CA5394: Unsichere Zufallszahlengeneratoren erkennen. |
| `dotnet_diagnostic.VSTHRD200.severity` | `none` | [AUS] VSTHRD200: Asynchrone Methoden mit Async-Suffix benennen. |

## 16 | Ausnahmen für [*.{tt.cs,feature.cs}]

Gültigkeitsbereich: `[*.{tt.cs,feature.cs}]`.

| Einstellung | Wert | Wirkung |
| --- | --- | --- |
| `dotnet_diagnostic.IDE0005.severity` | `none` | [AUS] IDE0005: Unnötige using-/Imports-Anweisungen entfernen. |
| `generated_code` | `true` | Dateien als generierten Code behandeln; viele Analyzer überspringen sie. |

## 17 | Ausnahmen für [*.csproj]

Gültigkeitsbereich: `[*.csproj]`.

| Einstellung | Wert | Wirkung |
| --- | --- | --- |
| `indent_size` | `2` | Anzahl der Leerzeichen pro Einrückungsebene. |
| `indent_style` | `space` | Mit Leerzeichen statt Tabulatoren einrücken. |

## 18 | Ausnahmen für [*.wixproj]

Gültigkeitsbereich: `[*.wixproj]`.

| Einstellung | Wert | Wirkung |
| --- | --- | --- |
| `indent_size` | `2` | Anzahl der Leerzeichen pro Einrückungsebene. |
| `indent_style` | `space` | Mit Leerzeichen statt Tabulatoren einrücken. |

## 19 | Ausnahmen für [Directory.Build.props]

Gültigkeitsbereich: `[Directory.Build.props]`.

| Einstellung | Wert | Wirkung |
| --- | --- | --- |
| `indent_size` | `2` | Anzahl der Leerzeichen pro Einrückungsebene. |
| `indent_style` | `space` | Mit Leerzeichen statt Tabulatoren einrücken. |

## 20 | Ausnahmen für [Directory.Build.targets]

Gültigkeitsbereich: `[Directory.Build.targets]`.

| Einstellung | Wert | Wirkung |
| --- | --- | --- |
| `indent_size` | `2` | Anzahl der Leerzeichen pro Einrückungsebene. |
| `indent_style` | `space` | Mit Leerzeichen statt Tabulatoren einrücken. |

## 21 | Ausnahmen für [*.runsettings]

Gültigkeitsbereich: `[*.runsettings]`.

| Einstellung | Wert | Wirkung |
| --- | --- | --- |
| `indent_size` | `2` | Anzahl der Leerzeichen pro Einrückungsebene. |
| `indent_style` | `space` | Mit Leerzeichen statt Tabulatoren einrücken. |

## 22 | Ausnahmen für [*.xml]

Gültigkeitsbereich: `[*.xml]`.

| Einstellung | Wert | Wirkung |
| --- | --- | --- |
| `indent_size` | `4` | Anzahl der Leerzeichen pro Einrückungsebene. |
| `indent_style` | `space` | Mit Leerzeichen statt Tabulatoren einrücken. |

## 23 | Ausnahmen für [app.config]

Gültigkeitsbereich: `[app.config]`.

| Einstellung | Wert | Wirkung |
| --- | --- | --- |
| `indent_size` | `2` | Anzahl der Leerzeichen pro Einrückungsebene. |
| `indent_style` | `space` | Mit Leerzeichen statt Tabulatoren einrücken. |

## Tests | tests/.editorconfig

Gültigkeitsbereich: `[*.cs]`.

Diese zusätzlichen Ausnahmen gelten im tests-Ordner und dessen Unterordnern, unabhängig vom Dateinamen.

| Einstellung | Wert | Wirkung |
| --- | --- | --- |
| `dotnet_diagnostic.CA2201.severity` | `none` | [AUS] CA2201: Zu allgemeine Exception-Typen nicht werfen. |
| `dotnet_diagnostic.CA5350.severity` | `none` | [AUS] CA5350: Schwache kryptografische Algorithmen wie SHA-1 erkennen. |
| `dotnet_diagnostic.CA5351.severity` | `none` | [AUS] CA5351: Gebrochene kryptografische Algorithmen wie MD5 erkennen. |

## Referenzen

- [EditorConfig-Spezifikation: Kommentare und Reihenfolge](https://spec.editorconfig.org/)
- [Microsoft: Analyzer-Stufen](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-options)
- [Microsoft: C#-Formatierung](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/csharp-formatting-options)
- [Microsoft: Stilregeln](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/)
- [Microsoft: Qualitätsregeln](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/)
- [StyleCop-Regeln](https://github.com/DotNetAnalyzers/StyleCopAnalyzers/tree/master/documentation)
- [AsyncFixer](https://github.com/semihokur/AsyncFixer)
- [Microsoft.Threading-Regeln](https://microsoft.github.io/vs-threading/analyzers/)
