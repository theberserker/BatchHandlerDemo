using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;

namespace BatchHandler.ConsoleApp
{
    class ExperimentalStuff
    {
        public ObservableCollection<string> Items { get; set; }
        public BindingList<string> BindList { get; set; }

        public void A()
        {
            Items.CollectionChanged += Items_CollectionChanged;
            BindList.AddingNew += BindList_AddingNew;
        }

        private void BindList_AddingNew(object sender, AddingNewEventArgs e)
        {
            //if (BindList.Count)
        }

        private void Items_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            //Items.Count
        }

        
    }
}
