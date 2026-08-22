using System;
using System.Collections.Generic;

namespace UsefulToolkit.Editor.WorkTrack
{
    /// <summary>
    /// WorkTrackが記録対象として認識しているUnityプロジェクトの情報。
    /// </summary>
    [Serializable]
    public class ProjectInfo
    {
        public string ProjectName;
        public string ProjectPath;
        public string CreatedAt;
    }

    /// <summary>
    /// JsonUtilityはルート要素が配列のJSONを扱えないため、Projects.json保存用に配列をラップする。
    /// </summary>
    [Serializable]
    internal class ProjectInfoListData
    {
        public List<ProjectInfo> Projects = new();
    }
}
