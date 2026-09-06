using UnityEngine;
using FreeFlow.Enums;

namespace FreeFlow.UI
{
    /// <summary>Base for every navigable screen and overlay. PageManager is the only thing that
    /// calls Open/Close -- a page must not activate/deactivate itself or a sibling page directly.</summary>
    public abstract class Page : MonoBehaviour
    {
        [SerializeField] private PageType pageType;

        public PageType PageType { get { return pageType; } }

        public virtual void Open()
        {
            gameObject.Activate();
        }

        public virtual void Close()
        {
            gameObject.Deactivate();
        }
    }
}
