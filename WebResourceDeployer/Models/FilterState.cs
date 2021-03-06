﻿using CrmDeveloperExtensions2.Core.DataGrid;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WebResourceDeployer.Models
{
    public class FilterState : INotifyPropertyChanged, IFilterProperty
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

        public static ObservableCollection<FilterState> CreateFilterList()
        {
            ObservableCollection<FilterState> filterStates = new ObservableCollection<FilterState> {
                new FilterState {Name = "Managed", Value = "Managed", IsSelected = false},
                new FilterState {Name = "Unmanaged", Value = "Unmanaged", IsSelected = true}
            };

            filterStates = new ObservableCollection<FilterState>(filterStates.OrderBy(e => e.Name));

            filterStates.Insert(0, new FilterState
            {
                Name = "Select All",
                Value = String.Empty
            });

            return filterStates;
        }
    }
}