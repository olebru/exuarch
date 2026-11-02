window.exuarchLines = {
    collections: {},
    hovered: {},

    editorFor: function (editorId) {
        return window.monaco.editor.getEditors().find(function (e) { const node = e.getContainerDomNode(); return node.id === editorId || node.closest('#' + editorId); });
    },

    highlight: function (editorId, lines, reveal) {
        const editor = this.editorFor(editorId);
        if (!editor) return;
        const decorations = lines.map(function (line) {
            return { range: { startLineNumber: line, startColumn: 1, endLineNumber: line, endColumn: 1 }, options: { isWholeLine: true, className: 'exuarch-linked-line', linesDecorationsClassName: 'exuarch-linked-gutter' } };
        });
        if (!this.collections[editorId] || this.collections[editorId].editor !== editor) this.collections[editorId] = { editor: editor, collection: editor.createDecorationsCollection() };
        this.collections[editorId].collection.set(decorations);
        if (reveal && lines.length > 0) editor.revealLinesInCenterIfOutsideViewport(Math.min.apply(null, lines), Math.max.apply(null, lines));
    },

    watchHover: function (editorId, service) {
        const editor = this.editorFor(editorId);
        if (!editor || this.hovered[editorId] === editor) return;
        this.hovered[editorId] = editor;
        let last = 0;
        const report = function (line) {
            if (line === last) return;
            last = line;
            void service.invokeMethodAsync('HoverLine', line);
        };
        editor.onMouseMove(function (e) { report(e.target.position ? e.target.position.lineNumber : 0); });
        editor.onMouseLeave(function () { report(0); });
    },
};

window.exuarchCSharp = {
    languageId: 'csharp',
    registered: false,
    services: {},

    serviceFor: function (model) {
        const editor = window.monaco.editor.getEditors().find(function (e) { return e.getModel() === model; });
        if (!editor) return null;
        const node = editor.getContainerDomNode();
        for (const id in this.services) if (node.id === id || node.closest('#' + id)) return this.services[id];
        return null;
    },

    attach: function (editorId, service) {
        const editor = window.exuarchLines.editorFor(editorId);
        if (!editor) return;
        this.services[editorId] = service;
        this.register();
        const tidy = (sparingCursorLine) => this.tidy(editor, service, sparingCursorLine).catch(() => {});
        let timer = null;
        let lastLine = 0;
        editor.onDidChangeModelContent(() => {
            clearTimeout(timer);
            timer = setTimeout(() => tidy(true), this.tidyDelay);
        });
        editor.onDidChangeCursorPosition(function (e) {
            if (e.position.lineNumber === lastLine) return;
            lastLine = e.position.lineNumber;
            void service.invokeMethodAsync('CaretLine', lastLine);
            void tidy(true);
        });
        editor.onDidBlurEditorText(() => tidy(false));
        editor.onDidDispose(() => { clearTimeout(timer); delete this.services[editorId]; });
    },

    tidyDelay: 600,

    tidy: async function (editor, service, sparingCursorLine) {
        const model = editor.getModel();
        if (!model) return;
        const version = model.getVersionId();
        const formatted = (await service.invokeMethodAsync('Format', model.getValue(), 0, 0)).split('\n');
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
        if (edits.length > 0) editor.executeEdits('sharpc-tidy', edits);
    },

    register: function () {
        if (this.registered) return;
        this.registered = true;
        const monaco = window.monaco;
        const id = this.languageId;
        const self = this;
        const kinds = {
            Keyword: monaco.languages.CompletionItemKind.Keyword, Type: monaco.languages.CompletionItemKind.Struct, Class: monaco.languages.CompletionItemKind.Class,
            Method: monaco.languages.CompletionItemKind.Method, Field: monaco.languages.CompletionItemKind.Field, Constant: monaco.languages.CompletionItemKind.Constant,
            Variable: monaco.languages.CompletionItemKind.Variable, Snippet: monaco.languages.CompletionItemKind.Snippet, Property: monaco.languages.CompletionItemKind.Property,
        };
        const call = async function (model, method, position) {
            const service = self.serviceFor(model);
            if (!service) return null;
            return service.invokeMethodAsync(method, model.getValue(), position ? position.lineNumber : 0, position ? position.column : 0);
        };
        const markdown = function (value) { return value ? { value: value } : undefined; };

        monaco.languages.registerCompletionItemProvider(id, {
            triggerCharacters: ['.'],
            provideCompletionItems: async function (model, position) {
                const json = await call(model, 'Complete', position);
                if (!json) return { suggestions: [] };
                const word = model.getWordUntilPosition(position);
                const range = { startLineNumber: position.lineNumber, endLineNumber: position.lineNumber, startColumn: word.startColumn, endColumn: word.endColumn };
                return {
                    suggestions: JSON.parse(json).map(function (item, index) {
                        return {
                            label: item.label,
                            kind: kinds[item.kind],
                            detail: item.detail || undefined,
                            documentation: markdown(item.documentation),
                            insertText: item.insertText,
                            insertTextRules: item.isSnippet ? monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet : undefined,
                            range: range,
                            sortText: String(index).padStart(4, '0'),
                        };
                    }),
                };
            },
        });
        monaco.languages.registerHoverProvider(id, {
            provideHover: async function (model, position) {
                const value = await call(model, 'Hover', position);
                return value ? { contents: [{ value: value }] } : null;
            },
        });
        monaco.languages.registerSignatureHelpProvider(id, {
            signatureHelpTriggerCharacters: ['(', ','],
            signatureHelpRetriggerCharacters: [','],
            provideSignatureHelp: async function (model, position) {
                const json = await call(model, 'SignatureHelp', position);
                if (!json) return null;
                const help = JSON.parse(json);
                return {
                    value: {
                        signatures: help.signatures.map(function (s) {
                            return { label: s.label, documentation: markdown(s.documentation), parameters: s.parameters.map(function (p) { return { label: p }; }) };
                        }),
                        activeSignature: Math.max(0, help.signatures.findIndex(function (s) { return s.parameters.length > help.activeParameter; })),
                        activeParameter: help.activeParameter,
                    },
                    dispose: function () { },
                };
            },
        });
        monaco.languages.registerDocumentFormattingEditProvider(id, {
            provideDocumentFormattingEdits: async function (model) {
                const text = await call(model, 'Format');
                return text === null ? [] : [{ range: model.getFullModelRange(), text: text }];
            },
        });
        monaco.languages.registerOnTypeFormattingEditProvider(id, {
            autoFormatTriggerCharacters: ['\n', '}', ';'],
            provideOnTypeFormattingEdits: async function (model, position, ch) {
                const line = ch === '\n' ? position.lineNumber - 1 : position.lineNumber;
                const lines = ch === '\n' ? [line, position.lineNumber] : [line];
                const edits = [];
                for (const target of lines) {
                    if (target < 1) continue;
                    const text = await call(model, 'FormatLine', { lineNumber: target, column: 1 });
                    if (text === null || text === model.getLineContent(target)) continue;
                    if (target === position.lineNumber && ch === '\n' && model.getLineContent(target).trim() === '') continue;
                    edits.push({ range: { startLineNumber: target, startColumn: 1, endLineNumber: target, endColumn: model.getLineMaxColumn(target) }, text: text });
                }
                return edits;
            },
        });
    },

    setMarkers: function (editorId, markersJson) {
        const editor = window.exuarchLines.editorFor(editorId);
        const model = editor ? editor.getModel() : null;
        if (!model) return;
        const monaco = window.monaco;
        monaco.editor.setModelMarkers(model, 'sharpc', JSON.parse(markersJson).map(function (m) {
            return { startLineNumber: m.line, endLineNumber: m.line, startColumn: m.startColumn, endColumn: m.endColumn, message: m.message, severity: monaco.MarkerSeverity.Error };
        }));
    },

    format: function (editorId) {
        const editor = window.exuarchLines.editorFor(editorId);
        if (editor) editor.getAction('editor.action.formatDocument').run();
    },

    copy: async function (text) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            return false;
        }
    },
};
