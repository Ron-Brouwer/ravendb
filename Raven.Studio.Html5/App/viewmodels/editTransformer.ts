﻿/// <reference path="../models/dto.ts" />

import viewModelBase = require("viewmodels/viewModelBase");
import transformer = require("models/transformer");
import saveTransformerCommand = require("commands/saveTransformerCommand");
import getSingleTransformerCommand = require("commands/getSingleTransformerCommand");
import deleteTransformerCommand = require("commands/deleteTransformerCommand");
import aceEditorBindingHandler = require("common/aceEditorBindingHandler");
import deleteTransformerConfirm = require("viewmodels/deleteTransformerConfirm");
import saveTransformerWithNewNameConfirm = require("viewmodels/saveTransformerWithNewNameConfirm");
import dialog = require("plugins/dialog");
import appUrl = require("common/appUrl");
import router = require("plugins/router");

class editTransformer extends viewModelBase {
    editedTransformer = ko.observable<transformer>();
    isEditingExistingTransformer = ko.observable(false);
    popoverOptions = ko.observable<any>();
    static containerSelector = "#editTransformerContainer";
    editorCollection = ko.observableArray<{ alias: string; controller: HTMLElement }>();
    
    private docEditor;

    constructor() {
        super();
        aceEditorBindingHandler.install();
    }

    activate(transformerToEditName: string) {
        super.activate(transformerToEditName);

        if (transformerToEditName) {
            this.isEditingExistingTransformer(true);
            this.editExistingTransformer(transformerToEditName);
        } else {
            this.editedTransformer(transformer.empty());
        }
        viewModelBase.dirtyFlag = new ko.DirtyFlag([this.editedTransformer().name, this.editedTransformer().transformResults]);
    }

    attached() {
        this.addTransformerHelpPopover();
        this.createKeyboardShortcut("alt+c", () => this.focusOnEditor(), editTransformer.containerSelector);
        this.createKeyboardShortcut("alt+shift+del", ()=> this.deleteTransformer(), editTransformer.containerSelector);
    }

    // Called back after the entire composition has finished (parents and children included)
    compositionComplete() {
        this.docEditor = ko.utils.domData.get($("#transformerAceEditor")[0], "aceEditor");
        this.focusOnEditor();
    }

    saveInObservable() {
        var docEditorText = this.docEditor.getSession().getValue();
        this.editedTransformer().transformResults(docEditorText);
    }

    addTransformerHelpPopover() {
        $("#transformerResultsLabel").popover({
            html: true,
            trigger: 'hover',
            content: 'The Transform function allows you to change the shape of individual result documents before the server returns them. It uses C# LINQ query syntax <br/> <br/> Example: <pre> <br/> <span class="code-keyword">from</span> order <span class="code-keyword">in</span> orders <br/> <span class="code-keyword">let</span> region = Database.Load(result.RegionId) <br/> <span class="code-keyword">select new</span> { <br/> result.Date, <br/> result.Amount, <br/> Region = region.Name, <br/> Manager = region.Manager <br/>}</pre>',
        });
    }

    focusOnEditor() {
        var editorElement = $("#transAceEditor").length == 1 ? $("#transAceEditor")[0] : null;
        if (editorElement) {
            if (this.docEditor) {
                this.docEditor.focus();
            }
        }
    }

    editExistingTransformer(unescapedTransformerName: string) {
        var indexName = decodeURIComponent(unescapedTransformerName);
        this.fetchTransformerToEdit(indexName)
            .done((trans: savedTransformerDto) => this.editedTransformer(new transformer().initFromSave(trans)));
    }
    
    fetchTransformerToEdit(transformerName: string): JQueryPromise<savedTransformerDto> {
        return new getSingleTransformerCommand(transformerName, this.activeDatabase()).execute();
    }

    saveTransformer() {
        if (this.isEditingExistingTransformer() && this.editedTransformer().wasNameChanged()) {
            var db = this.activeDatabase();
            var saveTransformerWithNewNameViewModel = new saveTransformerWithNewNameConfirm(this.editedTransformer(), db);
            saveTransformerWithNewNameViewModel.saveTask.done((trans: transformer) => this.editedTransformer(trans));
            dialog.show(saveTransformerWithNewNameViewModel);

        } else {

            new saveTransformerCommand(this.editedTransformer(), this.activeDatabase())
                .execute()
                .done(() => {
                    //this.editedTransformer(trans);
                    if (!this.isEditingExistingTransformer()) {
                        this.isEditingExistingTransformer(true);
                    }
                });
        }

        // Resync Changes
        viewModelBase.dirtyFlag().reset();
    }

    deleteTransformer() {
        var transformer = this.editedTransformer();
        
        if (transformer) {
            var db = this.activeDatabase();
            var deleteViewmodel = new deleteTransformerConfirm([transformer.name()], db);
            deleteViewmodel.deleteTask.done(() => {
                // Resync Changes
                viewModelBase.dirtyFlag().reset();
                router.navigate(appUrl.forTransformers(db));
            });
            dialog.show(deleteViewmodel);
        }
    }
}

export = editTransformer;