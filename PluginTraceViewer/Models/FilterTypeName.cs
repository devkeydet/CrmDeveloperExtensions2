﻿using CrmDeveloperExtensions2.Core.DataGrid;
using PluginTraceViewer.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace PluginTraceViewer.Models
{
    public class FilterTypeName : INotifyPropertyChanged, IFilterProperty
    {
        private bool _isSelected;

        public string Name { get; set; }
        public string Value { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public static ObservableCollection<FilterTypeName> CreateFilterList(ObservableCollection<CrmPluginTrace> traces)
        {
            ObservableCollection<FilterTypeName> filterTypeNames = new ObservableCollection<FilterTypeName>(traces.GroupBy(t => t.TypeName).Select(x =>
                new FilterTypeName
                {
                    Name = x.Key,
                    Value = x.Key,
                    IsSelected = true
                }).ToList());

            filterTypeNames = new ObservableCollection<FilterTypeName>(filterTypeNames.OrderBy(e => e.Name));

            filterTypeNames.Insert(0, new FilterTypeName
            {
                Name = "Select All",
                Value = String.Empty,
                IsSelected = true
            });

            return filterTypeNames;
        }
    }
}