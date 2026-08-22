namespace UsefulToolkit.Editor.Ai
{
    public interface ISetCommand
    {
        public abstract string Execute(string argument);

        public string Description => GetType().Name;

        static bool GetFromAccessToken(string accessToken, out string result)
        {
            return AccessTokenDatabase.Instance.TryResolveToken(accessToken, out result);
        }
    }
}