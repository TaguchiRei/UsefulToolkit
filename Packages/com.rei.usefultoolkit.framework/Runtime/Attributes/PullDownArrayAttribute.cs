using UnityEngine;

namespace UsefulToolkit.Attributes
{
    public class PullDownArrayAttribute : PropertyAttribute
    {
        public string MemberName { get; }

        public PullDownArrayAttribute(string memberName)
        {
            MemberName = memberName;
        }
    }
}