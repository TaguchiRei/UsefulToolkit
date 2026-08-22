using System;
using System.Collections.Generic;

namespace UsefulToolkit.Editor.WorkTrack
{
    /// <summary>
    /// 1回の作業セッションを表すレコード。日時はISO 8601形式(UTC)の文字列で保持する。
    /// </summary>
    [Serializable]
    public class WorkSession
    {
        public string SessionId;
        public string ProjectName;
        public string ProjectPath;
        public string StartTime;
        public string EndTime;
        public string Memo;
        public string[] Tags;
        public string CreatedAt;
        public string UpdatedAt;
    }

    /// <summary>
    /// JsonUtilityはルート要素が配列のJSONを扱えないため、Sessions.json保存用に配列をラップする。
    /// </summary>
    [Serializable]
    internal class WorkSessionListData
    {
        public List<WorkSession> Sessions = new();
    }
}
