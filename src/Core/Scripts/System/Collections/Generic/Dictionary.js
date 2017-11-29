﻿function Dictionary(dictionary) {
    this._instance = dictionary || {};

    createPropertyGet(this, "values", function () {
        return Object.values(this._instance);
    });

    createPropertyGet(this, "keys", function () {
        return Object.keys(this._instance);
    });

    createPropertyGet(this, "count", function () {
        return keyCount(this._instance);
    });
}

var Dictionary$ = {
    add: function (key, value) {
        this._instance[key] = value;
    },
    remove: function (key) {
        delete this._instance[key];
    },
    clear: function () {
        this._instance = {};
    },
    containsKey: function (key) {
        return this._instance[key] !== undefined;
    },
    tryGetValue: function (key, valueContainer) {
        var value = this._instance[key];
        if (value !== undefined){
            valueContainer.value = value;
            return true;
        }
        return false;
    },
    getEnumerator: function () {
        //TODO: Figure out the dictionary enumerator
        return null;
    }
}