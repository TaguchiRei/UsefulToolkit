using System;
using UsefulToolkit.Framework.BlackBoard;

namespace UsefulToolkit.Framework
{
    /// <summary>
    /// Stateクラスに付け、どのボードに登録されるかを公開する
    /// </summary>
    public class RegisterBoardAttribute : Attribute
    {
        public Type BoardType;

        public RegisterBoardAttribute(Type type)
        {
            if (!typeof(ChildStateBoardBase).IsAssignableFrom(type))
            {
                throw new ArgumentException("Type " + type + " is not assignable to " + typeof(ChildStateBoardBase));
            }

            BoardType = type;
        }
    }
}