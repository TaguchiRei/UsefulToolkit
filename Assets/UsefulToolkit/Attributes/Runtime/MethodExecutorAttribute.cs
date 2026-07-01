using System;

namespace UsefulToolkit.Attributes
{
    [AttributeUsage(AttributeTargets.Method)]
    public class MethodExecutorAttribute : Attribute
    {
        public string ButtonName { get; }
        public bool CanExecuteInEditMode { get; }

        public MethodExecutorAttribute(string buttonName, bool canExecuteInEditMode)
        {
            ButtonName = buttonName;
            CanExecuteInEditMode = canExecuteInEditMode;
        }

        public MethodExecutorAttribute(bool canExecuteInEditMode)
        {
            ButtonName = "Test";
            CanExecuteInEditMode = canExecuteInEditMode;
        }

        public MethodExecutorAttribute(string buttonName)
        {
            ButtonName = buttonName;
            CanExecuteInEditMode = false;
        }

        public MethodExecutorAttribute()
        {
            ButtonName = "Test";
            CanExecuteInEditMode = false;
        }
    }
}