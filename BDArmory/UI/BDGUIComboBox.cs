using UnityEngine;

namespace BDArmory.UI
{
    public class BDGUIComboBox
    {
        public bool IsOpen => isOpen;
        public float Height => scrollViewRect.height;

        Rect buttonRect;
        Rect listRect;
        GUIContent buttonContent;
        GUIContent[] listContent;
        float maxHeight;
        GUIStyle listStyle;
        int columns;
        bool persistant;

        bool isClickedComboButton = false;
        bool isOpen = false;
        int selectedItemIndex = -1;
        Vector2 scrollViewVector;
        Rect scrollViewRect;
        Rect scrollViewInnerRect;
        Rect selectionGridRect;
        RectOffset selectionGridRectOffset = new RectOffset(3, 3, 3, 3);
        float listHeight;
        float vScrollWidth = BDArmorySetup.BDGuiSkin.verticalScrollbar.fixedWidth + BDArmorySetup.BDGuiSkin.verticalScrollbar.margin.left;

        /// <summary>
        /// A drop-down combo-box.
        /// </summary>
        /// <param name="buttonRect">The rect for the button.</param>
        /// <param name="listRect">The rect defining the position and width of the selection grid. The height will be adjusted according to the contents.</param>
        /// <param name="buttonContent">The button content.</param>
        /// <param name="listContent">The selection grid contents.</param>
        /// <param name="maxHeight">The maximum height of the grid before scrolling is enabled.</param>
        /// <param name="listStyle">The GUIStyle to use for the selection grid.</param>
        /// <param name="columns">The number of columns in the selection grid.</param>
        /// <param name="persistant">Does the box remain open after clicking a selection</param>
        public BDGUIComboBox(Rect buttonRect, Rect listRect, GUIContent buttonContent, GUIContent[] listContent, float maxHeight, GUIStyle listStyle, int columns = 2, bool persistant = false)
        {
            this.buttonRect = buttonRect;
            this.listRect = listRect;
            this.buttonContent = buttonContent;
            this.listStyle = new GUIStyle(listStyle);
            this.listStyle.active.textColor = Color.black;
            this.listStyle.hover.textColor = Color.black;
            this.maxHeight = maxHeight;
            this.columns = columns;
            this.persistant = persistant;
            UpdateContent(listContent);
        }

        /// <summary>
        /// Display the button and combo-box.
        /// </summary>
        /// <returns>The selected item's index.</returns>
        public int Show()
        {
            if (GUI.Button(buttonRect, buttonContent, BDArmorySetup.ButtonStyle)) // Button
            {
                isClickedComboButton = !isClickedComboButton;
            }
            isOpen = isClickedComboButton; // Flag indicating if the selection grid open this frame.

            if (isClickedComboButton) // Selection grid
            {
                scrollViewVector = GUI.BeginScrollView(scrollViewRect, scrollViewVector, scrollViewInnerRect, BDArmorySetup.BDGuiSkin.horizontalScrollbar, BDArmorySetup.BDGuiSkin.verticalScrollbar);
                GUI.Box(scrollViewInnerRect, "", BDArmorySetup.SelectedButtonStyle); // Background box in the scroll view.
                if (selectedItemIndex != (selectedItemIndex = GUI.SelectionGrid(selectionGridRect, selectedItemIndex, listContent, columns, listStyle))) // If the selection is changed, then update the UI and close the combo-box.
                {
                    if (selectedItemIndex > -1) buttonContent.text = listContent[selectedItemIndex].text;
                    if (!persistant) isClickedComboButton = false;
                }
                GUI.EndScrollView();
            }

            return selectedItemIndex;
        }

        /// <summary>
        /// Externally set the selected item index.
        /// </summary>
        /// <param name="index"></param>
        public int SetSelectedItemIndex(int index)
        {
            selectedItemIndex = index;
            return selectedItemIndex;
        }

        /// <summary>
        /// Update internal rects when the button rect has moved.
        /// </summary>
        /// <param name="updatedButtonRect"></param>
        public void UpdateRect(Rect updatedButtonRect)
        {
            if (updatedButtonRect == buttonRect) return;
            listRect.x += updatedButtonRect.x - buttonRect.x;
            listRect.y += updatedButtonRect.y - buttonRect.y;
            buttonRect = updatedButtonRect;
            UpdateScrollViewRect();
        }

        /// <summary>
        /// Update the content of the combobox and recalculate sizes.
        /// </summary>
        /// <param name="content"></param>
        public void UpdateContent(GUIContent[] content)
        {
            listContent = content;
            var itemHeight = listStyle.CalcHeight(listContent[0], listRect.width / columns) + listStyle.margin.bottom;
            listHeight = itemHeight * Mathf.CeilToInt(listContent.Length / (float)columns);
            UpdateScrollViewRect();
        }

        /// <summary>
        /// Update the rects in the scroll view.
        /// </summary>
        void UpdateScrollViewRect()
        {
            scrollViewRect = new Rect(listRect.x, listRect.y + listRect.height, listRect.width, Math.Min(maxHeight, listHeight + selectionGridRectOffset.vertical));
            scrollViewInnerRect = new Rect(0, 0, scrollViewRect.width, listHeight + selectionGridRectOffset.bottom);
            if (scrollViewInnerRect.height > scrollViewRect.height) scrollViewInnerRect.width -= vScrollWidth;
            selectionGridRect = selectionGridRectOffset.Remove(scrollViewInnerRect);
        }
    }
}
