using System.Collections.Generic;
using System;
using UnityEngine;
using UsefulToolkit.Editor.Setting;

namespace UsefulToolkit.Editor.GitSupport
{
    [CreateAssetMenu(fileName = "GitSupportSettings", menuName = "UsefulToolkit/GitSupport Settings")]
    public sealed class GitSupportSettings : LocalSettingBase<GitSupportSettings>
    {
        public List<string> WarningBranches = new List<string> { "develop", "main", "master" };
        public BranchWarningType WarningType = BranchWarningType.Warning;
        public bool WarningOnSaved = true;
        public bool WarningOnCompiled = true;
    }

    public enum BranchWarningType
    {
        None,
        Warning,
        CantSave
    }
}