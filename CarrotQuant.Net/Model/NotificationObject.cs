﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace CarrotQuant.Net.Model
{
    public abstract class NotificationObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected void RaisePropertyChanged<T>(Expression<Func<T>> action)
        {
            object propertyName = GetPropertyName(action);
            RaisePropertyChanged(propertyName as string);
        }

        private object GetPropertyName<T>(Expression<Func<T>> action)
        {
            MemberExpression expression = (MemberExpression)action.Body;
            string propertyName = expression.Member.Name;
            return propertyName;
        }
    }
}
