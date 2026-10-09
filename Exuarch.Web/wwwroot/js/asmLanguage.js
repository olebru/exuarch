// Monaco support for the assembly language. Highlighting runs in the browser from the current mnemonic list; completion,
// hover and formatting call back into the C# language service (AssemblyLanguage) through a .NET object reference.
window.exuarchAsm = {
    languageId: 'exuarch-asm',
    service: null,
    registered: false,
    services: {},
    margins: {},
    tidyDelay: 600,

    idOf: function (editor) {
        const node = editor.getContainerDomNode();
        for (const id in this.services) if (node.id === id || node.closest('#' + id)) return id;
        return null;
    },

    serviceFor: function (model) {
        const editor = window.monaco.editor.getEditors().find(function (e) { return e.getModel() === model; });
        const id = editor ? this.idOf(editor) : null;
        return (id && this.services[id]) || this.service;
    },

    editorFor: function (editorId) {
        return window.monaco.editor.getEditors().find(function (e) { const node = e.getContainerDomNode(); return node.id === editorId || node.closest('#' + editorId); });
    },

    attach: function (editorId, service, margin) {
        const editor = this.editorFor(editorId);
        if (!editor) return;
        this.services[editorId] = service;
        if (margin) {
            this.margins[editorId] = [];
            editor.updateOptions({ lineNumbers: (line) => this.margins[editorId][line - 1] || '' });
        }
        const tidy = (sparingCursorLine) => this.tidy(editorId, sparingCursorLine).catch(() => {});
        let timer = null;
        let lastLine = editor.getPosition() ? editor.getPosition().lineNumber : 1;
        editor.onDidChangeModelContent(() => {
            clearTimeout(timer);
            timer = setTimeout(() => tidy(true), this.tidyDelay);
        });
        editor.onDidChangeCursorPosition((e) => {
            if (e.position.lineNumber === lastLine) return;
            lastLine = e.position.lineNumber;
            void tidy(true);
        });
        editor.onDidBlurEditorText(() => tidy(false));
        editor.onDidDispose(() => { clearTimeout(timer); delete this.services[editorId]; delete this.margins[editorId]; });
    },

    tidy: async function (editorId, sparingCursorLine) {
        const editor = this.editorFor(editorId);
        const model = editor ? editor.getModel() : null;
        const service = this.services[editorId];
        if (!model || !service) return;
        const source = model.getValue();
        const version = model.getVersionId();
        const formatted = (await service.invokeMethodAsync('Format', source)).split('\n');
        if (model.isDisposed() || model.getVersionId() !== version) return;
        const spared = sparingCursorLine && editor.hasTextFocus() && editor.getPosition() ? editor.getPosition().lineNumber : -1;
        const edits = [];
        for (let line = 1; line <= Math.min(model.getLineCount(), formatted.length); line++) {
            if (line === spared) continue;
            const text = formatted[line - 1];
            if (text !== model.getLineContent(line)) {
                edits.push({ range: { startLineNumber: line, startColumn: 1, endLineNumber: line, endColumn: model.getLineMaxColumn(line) }, text: text });
            }
        }
        if (edits.length > 0) editor.executeEdits('exuarch-tidy', edits);
    },

    setMargin: function (editorId, margin) {
        if (!(editorId in this.margins)) return;
        this.margins[editorId] = margin;
        const editor = this.editorFor(editorId);
        if (editor) editor.updateOptions({ lineNumbersMinChars: 5 });
    },

    // Called by the editor component once Monaco is loaded, and again whenever the instruction set changes.
    register: function (service, mnemonics) {
        const monaco = window.monaco;
        this.service = service;
        const id = this.languageId;
        if (!this.registered) {
            this.registered = true;
            // Links in hovers written exuarch:guide/... open the handbook. Monaco drops links with schemes it does not
            // know, so the hover turns them into this command, which trusted hover text may run.
            const language = this;
            monaco.editor.registerCommand('exuarch.open', function (accessor, href) {
                language.service.invokeMethodAsync('OpenLink', href);
            });
            monaco.languages.register({ id: id });
            monaco.languages.setLanguageConfiguration(id, {
                comments: { lineComment: ';' },
                wordPattern: /\.?[A-Za-z_][A-Za-z0-9_]*/,
                autoClosingPairs: [{ open: '"', close: '"', notIn: ['string', 'comment'] }],
            });
            // The light and the dark theme are one design in two sets of colours; exuarchTheme in machineEditor.js
            // picks between them.
            for (const [name, base, c] of [
                ['exuarch', 'vs', {
                    ink: '17181c', directive: '0c8599', label: '7c5cc4', number: '2f6fdb', string: 'b8340f', comment: '9a9ea6', delimiter: '7d828c',
                    surface: '#ffffff', gutter: '#faf9f6', lineNumber: '#b9bcc2', cursor: '#f04f23', selection: '#fde6de', inactive: '#f3f2ee', guide: '#ebe9e3', border: '#cfccc2',
                }],
                ['exuarch-dark', 'vs-dark', {
                    ink: 'e9e7e2', directive: '3bc2d4', label: 'a58fe2', number: '6f9ff2', string: 'ff9d7c', comment: '7a7f89', delimiter: '8b909a',
                    surface: '#1b1e23', gutter: '#20242a', lineNumber: '#5f646d', cursor: '#ff6a3d', selection: '#3b2119', inactive: '#2b2f36', guide: '#2b2f36', border: '#3b404a',
                }],
            ]) {
                monaco.editor.defineTheme(name, {
                    base: base,
                    inherit: true,
                    rules: [
                        { token: 'keyword', foreground: c.ink, fontStyle: 'bold' },
                        { token: 'keyword.directive', foreground: c.directive, fontStyle: 'bold' },
                        { token: 'type.label', foreground: c.label, fontStyle: 'bold' },
                        { token: 'identifier.label', foreground: c.label },
                        { token: 'number', foreground: c.number },
                        { token: 'string', foreground: c.string },
                        { token: 'comment', foreground: c.comment },
                        { token: 'delimiter', foreground: c.delimiter },
                    ],
                    colors: {
                        'editor.background': c.surface,
                        'editor.foreground': '#' + c.ink,
                        'editor.lineHighlightBackground': c.gutter,
                        'editor.lineHighlightBorder': '#00000000',
                        'editorLineNumber.foreground': c.lineNumber,
                        'editorLineNumber.activeForeground': '#' + c.ink,
                        'editorGutter.background': c.gutter,
                        'editorCursor.foreground': c.cursor,
                        'editor.selectionBackground': c.selection,
                        'editor.inactiveSelectionBackground': c.inactive,
                        'editorIndentGuide.background1': c.guide,
                        'editorWidget.background': c.surface,
                        'editorWidget.border': c.border,
                        'editorSuggestWidget.selectedBackground': c.inactive,
                        'editorHoverWidget.background': c.surface,
                        'editorHoverWidget.border': c.border,
                    },
                });
            }

            const self = this;
            monaco.languages.registerCompletionItemProvider(id, {
                triggerCharacters: ['.', ' ', ','],
                provideCompletionItems: async function (model, position) {
                    // The typed part of the token replaced by a suggestion. A lone '.' is not a word to Monaco (the
                    // word pattern needs a letter after it), so it is matched here; otherwise picking .DATA after
                    // typing '.' would give '..DATA'.
                    const before = model.getLineContent(position.lineNumber).substring(0, position.column - 1);
                    const typed = /\.?[A-Za-z0-9_]*$/.exec(before)[0];
                    const range = { startLineNumber: position.lineNumber, endLineNumber: position.lineNumber, startColumn: position.column - typed.length, endColumn: position.column };
                    const items = JSON.parse(await self.serviceFor(model).invokeMethodAsync('Complete', model.getValue(), position.lineNumber, position.column));
                    const kinds = { 0: monaco.languages.CompletionItemKind.Function, 1: monaco.languages.CompletionItemKind.Keyword, 2: monaco.languages.CompletionItemKind.Reference };
                    return {
                        suggestions: items.map(function (item, index) {
                            return {
                                label: { label: item.label, description: item.detail },
                                kind: kinds[item.kind],
                                detail: item.detail,
                                documentation: item.documentation,
                                insertText: item.insertText,
                                range: range,
                                sortText: String(index).padStart(4, '0'),
                                command: item.insertText.endsWith(' ') ? { id: 'editor.action.triggerSuggest', title: 'operands' } : undefined,
                            };
                        }),
                    };
                },
            });
            monaco.languages.registerHoverProvider(id, {
                provideHover: async function (model, position) {
                    const markdown = await self.serviceFor(model).invokeMethodAsync('Hover', model.getValue(), position.lineNumber, position.column);
                    if (!markdown) return null;
                    const value = markdown.replace(/\]\((exuarch:[^)]+)\)/g, function (match, href) {
                        return '](command:exuarch.open?' + encodeURIComponent(JSON.stringify([href])) + ')';
                    });
                    // Trusted only to run exuarch.open: the text can come from an imported machine's instruction descriptions.
                    return { contents: [{ value: value, isTrusted: { enabledCommands: ['exuarch.open'] } }] };
                },
            });
            monaco.languages.registerDocumentFormattingEditProvider(id, {
                provideDocumentFormattingEdits: async function (model) {
                    const text = await self.serviceFor(model).invokeMethodAsync('Format', model.getValue());
                    return [{ range: model.getFullModelRange(), text: text }];
                },
            });
            // Used by format on paste: formats with the whole document's columns, changing only lines in the range.
            monaco.languages.registerDocumentRangeFormattingEditProvider(id, {
                provideDocumentRangeFormattingEdits: async function (model, range) {
                    const formatted = (await self.serviceFor(model).invokeMethodAsync('Format', model.getValue())).split('\n');
                    const edits = [];
                    for (let line = range.startLineNumber; line <= Math.min(range.endLineNumber, model.getLineCount()); line++) {
                        const text = formatted[line - 1];
                        if (text !== undefined && text !== model.getLineContent(line)) {
                            edits.push({ range: { startLineNumber: line, startColumn: 1, endLineNumber: line, endColumn: model.getLineMaxColumn(line) }, text: text });
                        }
                    }
                    return edits;
                },
            });
            // Format on type: when Enter is pressed, tidy the line that was just finished.
            monaco.languages.registerOnTypeFormattingEditProvider(id, {
                autoFormatTriggerCharacters: ['\n'],
                provideOnTypeFormattingEdits: async function (model, position) {
                    const line = position.lineNumber - 1;
                    if (line < 1) return [];
                    const text = await self.serviceFor(model).invokeMethodAsync('FormatLine', model.getValue(), line);
                    if (text === model.getLineContent(line)) return [];
                    return [{ range: { startLineNumber: line, startColumn: 1, endLineNumber: line, endColumn: model.getLineMaxColumn(line) }, text: text }];
                },
            });
        }
        monaco.languages.setMonarchTokensProvider(id, {
            ignoreCase: true,
            mnemonics: mnemonics,
            tokenizer: {
                root: [
                    [/;.*$/, 'comment'],
                    [/[A-Za-z_][A-Za-z0-9_]*:/, 'type.label'],
                    [/\.[A-Za-z]+/, 'keyword.directive'],
                    [/#?0[xX][0-9A-Fa-f]+/, 'number'],
                    [/#?[0-9]+/, 'number'],
                    [/#?'(\\.|[^'\\])'/, 'string'],
                    [/"([^"\\]|\\.)*"/, 'string'],
                    [/"[^"]*$/, 'string'],
                    [/[A-Za-z_][A-Za-z0-9_]*/, { cases: { '@mnemonics': 'keyword', '@default': 'identifier.label' } }],
                    [/,/, 'delimiter'],
                ],
            },
        });
    },

    // Replaces the problem markers on the editor's model.
    setMarkers: function (editorId, markersJson) {
        const monaco = window.monaco;
        const markers = JSON.parse(markersJson);
        const editor = monaco.editor.getEditors().find(function (e) { return e.getContainerDomNode().id === editorId || e.getContainerDomNode().closest('#' + editorId); });
        const model = editor ? editor.getModel() : null;
        if (!model) return;
        monaco.editor.setModelMarkers(model, 'exuarch-asm', markers.map(function (m) {
            return {
                startLineNumber: m.line, endLineNumber: m.line, startColumn: m.startColumn, endColumn: m.endColumn,
                message: m.message, severity: m.warning ? monaco.MarkerSeverity.Warning : monaco.MarkerSeverity.Error,
            };
        }));
    },

    // Runs Monaco's format document action on the editor.
    format: function (editorId) {
        const editor = window.monaco.editor.getEditors().find(function (e) { return e.getContainerDomNode().closest('#' + editorId); });
        if (editor) editor.getAction('editor.action.formatDocument').run();
    },
};
