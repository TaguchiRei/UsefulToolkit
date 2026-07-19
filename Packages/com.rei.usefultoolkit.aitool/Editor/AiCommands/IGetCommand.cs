namespace UsefulToolkit.Ai
{
    public interface IGetCommand
    {
        public string Execute(string argument);

        public string Description => GetType().Name;

        static string GetAccessToken(string linkingTarget)
        {
            return AccessTokenDatabase.Instance.GetOrCreateToken(linkingTarget);
        }
    }
}