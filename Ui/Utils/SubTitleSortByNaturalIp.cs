using System;
using System.Collections;
using _1RM.View;

namespace _1RM.Utils
{
    /// <summary>
    /// The <c>IComparer</c> the server list hands to its <c>CollectionViewSource</c> to sort the address
    /// column. It is a thin wrapper: it pulls the two subtitles out of the view models and defers the actual
    /// ordering to <see cref="HostNaturalSort"/>, which is pure and unit-tested. The only thing that lives
    /// here is the WPF-specific plumbing — reading <c>SubTitle</c> and applying the sort direction.
    /// </summary>
    public class SubTitleSortByNaturalIp : IComparer
    {
        private readonly bool _orderIsAsc;

        public SubTitleSortByNaturalIp(bool orderIsAsc)
        {
            _orderIsAsc = orderIsAsc;
        }

        public int Compare(object? x, object? y)
        {
            if (x is not ProtocolBaseViewModel px || y is not ProtocolBaseViewModel py)
            {
                // Anything that is not a server row keeps the previous behaviour of being pushed to one end.
                return _orderIsAsc ? -1 : 1;
            }

            var cmp = HostNaturalSort.Compare(px.SubTitle, py.SubTitle);
            return _orderIsAsc ? cmp : -cmp;
        }
    }
}
