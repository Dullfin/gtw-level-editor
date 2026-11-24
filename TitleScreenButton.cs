using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GTWLevelEditor
{
    /// <summary>
    /// Adds a "Level Editor" folder tab to the title screen: a copy of the game's own
    /// Doinkler Portfolio button, placed in the bottom slot with the game's two tabs moved up one.
    /// </summary>
    public partial class LevelEditorController
    {
        private const string MainMenuPath = "Main Menu Canvas/UI_MainMenu";
        private GameObject _titleButton;

        /// <summary>Called every frame on the title screen; the title scene is rebuilt each visit.</summary>
        private void EnsureTitleButton()
        {
            if (_titleButton != null) return;
            var portfolio = GameObject.Find(MainMenuPath + "/btn_DoinklerPortfolio");
            if (portfolio == null) return;
            var parent = portfolio.transform.parent;
            var existing = parent.Find("btn_LevelEditor");
            if (existing != null) { _titleButton = existing.gameObject; return; }

            var special = parent.Find("btn_DoinklerSpecial");
            var portfolioRect = (RectTransform)portfolio.transform;
            var specialRect = special as RectTransform;
            float spacing = specialRect != null ? portfolioRect.anchoredPosition.y - specialRect.anchoredPosition.y : 166.5f;
            var bottomSlot = specialRect != null ? specialRect.anchoredPosition : portfolioRect.anchoredPosition - new Vector2(0f, spacing);

            var clone = Instantiate(portfolio, parent, false);
            clone.name = "btn_LevelEditor";
            ((RectTransform)clone.transform).anchoredPosition = bottomSlot;
            clone.transform.SetSiblingIndex(portfolio.transform.GetSiblingIndex() + 2);

            // The game's two tabs move up one slot.
            portfolioRect.anchoredPosition += new Vector2(0f, spacing);
            if (specialRect != null) specialRect.anchoredPosition += new Vector2(0f, spacing);

            // Our own title: drop the translation component first, or it puts the game's text back.
            foreach (var mb in clone.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && mb.GetType().Name == "Localize") DestroyImmediate(mb);
            foreach (var text in clone.GetComponentsInChildren<TMP_Text>(true))
                if (text.name == "title_ChallengerLvls") text.text = "Level Editor";
            var badge = clone.transform.Find("img_NewNotif");
            if (badge != null) badge.gameObject.SetActive(false);

            var button = clone.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(ToggleTitleMenu);
            var nav = button.navigation;
            nav.mode = Navigation.Mode.Automatic;
            button.navigation = nav;

            _titleButton = clone;
        }

        private void ToggleTitleMenu()
        {
            _titleMenuOpen = !_titleMenuOpen;
            if (!_titleMenuOpen) return;
            RefreshLevelList();
            _environmentNames = CustomSession.EnvironmentNames();
            _titleError = _environmentNames.Count == 0 ? "The Doinkler level select wasn't found on this screen." : null;
        }
    }
}
