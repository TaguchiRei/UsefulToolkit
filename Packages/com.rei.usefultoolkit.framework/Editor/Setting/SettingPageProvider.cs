using System.Collections.Generic;
using System.Linq;

namespace UsefulToolkit.Framework
{
    public class SettingPageProvider
    {
        private List<SettingPageBase> _pages;
        public IReadOnlyList<SettingPageBase> Pages => _pages;

        public SettingPageProvider()
        {
            Reload();
        }

        public void Reload()
        {
            _pages = InstanceCollector.GetInstances<SettingPageBase>(true)
                .OrderBy(p => p.Order)
                .ThenBy(p => p.Name)
                .ToList();
        }
    }
}