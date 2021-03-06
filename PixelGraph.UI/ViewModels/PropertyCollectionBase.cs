﻿using PixelGraph.UI.Models.PropertyGrid;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace PixelGraph.UI.ViewModels
{
    public abstract class PropertyCollectionBase<TSource> : List<IEditPropertyRow<TSource>>, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;


        public void AddText<TValue>(string displayName, string propertyName, TValue defaultValue = default)
        {
            var row = new EditTextPropertyRowModel<TSource, TValue>(displayName, propertyName, defaultValue);
            row.ValueChanged += OnRowValueChanged;
            Add(row);
        }

        public void AddBool<TValue>(string displayName, string propertyName, TValue defaultValue = default)
        {
            var row = new EditBoolPropertyRowModel<TSource, TValue>(displayName, propertyName, defaultValue);
            row.ValueChanged += OnRowValueChanged;
            Add(row);
        }

        public void AddSelect<TValue>(string displayName, string propertyName, SelectPropertyRowOptions options, TValue defaultValue = default)
        {
            var row = new EditSelectPropertyRowModel<TSource, TValue>(displayName, propertyName, options, defaultValue);
            row.ValueChanged += OnRowValueChanged;
            Add(row);
        }

        private void OnRowValueChanged(object sender, PropertyValueChangedEventArgs e)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(e.PropertyName));
        }

        public void SetData(TSource data)
        {
            foreach (var row in this)
                row.SetData(data);
        }

        public void Invalidate()
        {
            foreach (var row in this)
                row.Invalidate();
        }
    }

    public class PropertyValueChangedEventArgs : EventArgs
    {
        public string PropertyName {get; set;}
    }
}
