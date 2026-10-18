using UnityEngine;
using UnityEngine.UI;
using xSkills.Skills;

namespace xSkills.GUI
{
    internal sealed partial class SkillGraphicalUI
    {
        private RectTransform transferOverlay;
        private SkillData transferSkill;
        private int transferLevels;
        private InputField transferRecipient;
        private InputField transferXpInput;
        private Text transferLevelsText;
        private Text transferSkillName;
        private bool transferOpen;

        private void BuildTransferPopup()
        {
            transferOverlay = UIFactory.Rect(window, "Experience transfer modal", 0, 0, WindowW, WindowH);
            UIFactory.Fill(transferOverlay, "Screen shade", 0, 0, WindowW, WindowH,
                new Color(0, 0, 0, .72f));
            // Подложка блокирует взаимодействие с деревом навыков
            UIFactory.Button(transferOverlay, "Modal backdrop", 0, 0, WindowW, WindowH, () => CloseTransfer());
            const float x = 515, y = 245, w = 770, h = 555;
            UIFactory.Image(transferOverlay, "Popup background", "elements", "bg", x, y, w, h);
            UIFactory.Image(transferOverlay, "Popup border", "elements", "frame", x, y, w, h, 60);
            UIFactory.Label(transferOverlay, "Popup title", Localizer.T("transferTitle"),
                x + 42, y + 30, 435, 49, 35, UIFactory.Gold);
            transferSkillName = UIFactory.Label(transferOverlay, "Popup skill", "", x + 465, y + 36,
                260, 39, 23, UIFactory.Gold, TextAnchor.MiddleRight);
            UIFactory.Image(transferOverlay, "Popup divider", "elements", "tooltip_sep", x + 40, y + 95, w - 80, 4);
            UIFactory.Label(transferOverlay, "Target label", Localizer.T("transferPlayer"),
                x + 50, y + 131, 200, 37, 23, UIFactory.White);
            transferRecipient = TransferInput(transferOverlay, "Recipient", x + 260, y + 131, 445, 40,
                Localizer.T("transferPlayerHint"), false);
            UIFactory.Label(transferOverlay, "XP label", Localizer.T("transferXp"),
                x + 50, y + 198, 200, 37, 23, UIFactory.White);
            transferXpInput = TransferInput(transferOverlay, "XP amount", x + 260, y + 198, 190, 40,
                "0", true);
            UIFactory.Label(transferOverlay, "Levels label", Localizer.T("transferLevels"),
                x + 50, y + 267, 200, 37, 23, UIFactory.White);
            UIFactory.Image(transferOverlay, "Minus bg", "elements", "button_idle", x + 260, y + 267, 45, 41, 2);
            UIFactory.Label(transferOverlay, "Minus sign", "−", x + 260, y + 267, 45, 41, 28,
                UIFactory.White, TextAnchor.MiddleCenter);
            UIFactory.Button(transferOverlay, "Minus", x + 260, y + 267, 45, 41, () => ChangeTransferLevels(-1));
            transferLevelsText = UIFactory.Label(transferOverlay, "Level amount", "0", x + 308, y + 267, 82, 41,
                23, UIFactory.White, TextAnchor.MiddleCenter);
            UIFactory.Image(transferOverlay, "Plus bg", "elements", "button_idle", x + 394, y + 267, 45, 41, 2);
            UIFactory.Label(transferOverlay, "Plus sign", "+", x + 394, y + 267, 45, 41, 28,
                UIFactory.White, TextAnchor.MiddleCenter);
            UIFactory.Button(transferOverlay, "Plus", x + 394, y + 267, 45, 41, () => ChangeTransferLevels(1));
            UIFactory.Label(transferOverlay, "Transfer state", Localizer.T("ui-transfer-not-available"),
                x + 50, y + 342, w - 100, 71, 19, UIFactory.Grey, TextAnchor.MiddleCenter);
            UIFactory.Image(transferOverlay, "Confirm bg", "elements", "button_idle", x + 290, y + 456, 186, 46, 2);
            UIFactory.Label(transferOverlay, "Confirm text", Localizer.T("transferConfirm"),
                x + 290, y + 456, 186, 46, 21, UIFactory.Grey, TextAnchor.MiddleCenter);
            // Подтверждение не посылается до появления сетевой системы опыта
            UIFactory.Image(transferOverlay, "Cancel bg", "elements", "button_idle", x + 510, y + 456, 186, 46, 2);
            UIFactory.Label(transferOverlay, "Cancel text", Localizer.T("transferCancel"),
                x + 510, y + 456, 186, 46, 21, UIFactory.White, TextAnchor.MiddleCenter);
            UIFactory.Button(transferOverlay, "Cancel", x + 510, y + 456, 186, 46, () => CloseTransfer());
            transferOverlay.gameObject.SetActive(false);
        }

        private static InputField TransferInput(Transform parent, string name, float x, float y,
            float width, float height, string hint, bool numbersOnly)
        {
            RectTransform rect = UIFactory.Rect(parent, name, x, y, width, height);
            Image background = rect.gameObject.AddComponent<Image>();
            background.sprite = ResourceLoader.Sprite("elements", "button_idle", 2);
            background.type = background.sprite != null ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            background.color = UIFactory.White;
            var field = rect.gameObject.AddComponent<InputField>();
            Text placeholder = UIFactory.Label(rect, "Placeholder", hint, 12, 3, width - 24, height - 6, 20, UIFactory.Grey);
            Text value = UIFactory.Label(rect, "Text", "", 12, 3, width - 24, height - 6, 20, UIFactory.White);
            value.raycastTarget = false;
            placeholder.GetComponent<BitmapFontLabel>().enabled = false;
            value.GetComponent<BitmapFontLabel>().enabled = false;
            field.textComponent = value;
            field.placeholder = placeholder;
            field.lineType = InputField.LineType.SingleLine;
            if (numbersOnly) field.contentType = InputField.ContentType.IntegerNumber;
            return field;
        }

        private void ChangeTransferLevels(int offset)
        {
            int max = transferSkill == null ? 0 : Mathf.Max(0, transferSkill.PreviewLevel - 1);
            transferLevels = Mathf.Clamp(transferLevels + offset, 0, max);
            transferLevelsText.text = transferLevels.ToString();
        }

        private void OpenTransfer(SkillData skill)
        {
            if (page != "_spec" || transferOverlay == null || skill == null) return;
            transferSkill = skill;
            transferLevels = 0;
            transferRecipient.text = "";
            transferXpInput.text = "0";
            transferLevelsText.text = "0";
            transferSkillName.text = Localizer.SkillName(skill.Id);
            transferOpen = true;
            transferOverlay.gameObject.SetActive(true);
            SoundPlayer.Play("pagesub");
        }

        private void CloseTransfer(bool sound = true)
        {
            if (!transferOpen) return;
            transferOpen = false;
            transferSkill = null;
            if (transferOverlay != null) transferOverlay.gameObject.SetActive(false);
            if (sound) SoundPlayer.Play("pagesub");
        }
    }
}
