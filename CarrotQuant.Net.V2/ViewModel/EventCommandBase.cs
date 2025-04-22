using Microsoft.Xaml.Behaviors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace CarrotQuant.Net.ViewModel
{
    public class EventCommandBase : TriggerAction<DependencyObject>
    {
        // 事件
        public static readonly DependencyProperty CommandProperty =
            DependencyProperty.Register("Command", typeof(ICommand), typeof(EventCommandBase), new PropertyMetadata(null));
        public ICommand Command
        {
            get => (ICommand)GetValue(CommandProperty);
            set => SetValue(CommandProperty, value);
        }

        // 事件参数
        public static readonly DependencyProperty CommandParameterProperty =
            DependencyProperty.Register("CommandParameter", typeof(object), typeof(EventCommandBase), new PropertyMetadata(null));
        public object CommandParameter
        {
            get => GetValue(CommandParameterProperty);
            set => SetValue(CommandParameterProperty, value);
        }

        // 事件命令
        protected override void Invoke(object parameter)
        {
            if (CommandParameter != null)
            {
                parameter = CommandParameter;
            }
            if (Command != null)
            {
                Command.Execute(parameter);
            }
        }
    }
}
