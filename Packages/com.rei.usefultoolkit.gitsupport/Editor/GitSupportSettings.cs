using System;
using System.Collections.Generic;
using UnityEngine;
using UsefulToolkit.Framework;

namespace UsefulToolkit.GitSupport
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