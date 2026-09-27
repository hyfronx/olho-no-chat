using System.Windows;
using System.Windows.Media;

namespace OlhoNoChat
{
    public static class VisualTreeHelperExtensions
    {
        public static T FindChildByType<T>(this DependencyObject parent, string typeName) where T : DependencyObject
        {
            if (parent == null) return null;

            T foundChild = null;

            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child.GetType().FullName == typeName)
                {
                    foundChild = (T)child;
                    break;
                }

                foundChild = FindChildByType<T>(child, typeName);
                if (foundChild != null) break;
            }

            return foundChild;
        }

        public static T FindChild<T>(this DependencyObject parent, string childName) where T : DependencyObject
        {
            if (parent == null) return null;

            T foundChild = null;

            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement element && element.Name == childName && child is T match)
                {
                    foundChild = match;
                    break;
                }

                foundChild = FindChild<T>(child, childName);
                if (foundChild != null) break;
            }

            return foundChild;
        }
    }

}
