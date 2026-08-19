using System;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 일반 인벤토리와 독립된 플레이어별 무전기 전용 슬롯.
/// 한 칸만 존재하며, 무전기 픽업 성공 시 비어 있음에서 채워짐으로 전환된다.
/// </summary>
public sealed class PlayerRadioSlot : NetworkBehaviour
{
    [Header("HUD")]
    [SerializeField] private RectTransform hudParent;
    [SerializeField] private InputReader inputReader;
    [SerializeField] private Vector2 hudPosition = new(395f, 20f);
    [SerializeField] private Vector2 hudSize = new(100f, 100f);
    [SerializeField] private Color emptyColor = new(0f, 0f, 0f, 0.5f);
    [SerializeField] private Color filledColor = new(0.1f, 0.55f, 0.2f, 0.8f);

    private readonly NetworkVariable<bool> isFilled = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    public bool IsFilled => isFilled.Value;
    public event Action Changed;

    private Image slotBackground;
    private TMP_Text slotLabel;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isFilled.OnValueChanged += OnFilledChanged;

        if (!IsOwner) return;

        BuildHud();
        RefreshView();
    }

    public override void OnNetworkDespawn()
    {
        isFilled.OnValueChanged -= OnFilledChanged;
        base.OnNetworkDespawn();
    }

    /// <summary>전용 칸이 비어 있을 때만 무전기를 장착한다.</summary>
    public bool TryFill()
    {
        if (!IsOwner || IsFilled) return false;

        isFilled.Value = true;
        RefreshView();
        Changed?.Invoke();
        return true;
    }

    private void OnFilledChanged(bool previousValue, bool newValue)
    {
        if (IsOwner)
        {
            RefreshView();
        }

        Changed?.Invoke();
    }

    private void RefreshView()
    {
        if (slotBackground != null)
        {
            slotBackground.color = IsFilled ? filledColor : emptyColor;
        }

        if (slotLabel != null)
        {
            string binding = inputReader != null
                ? inputReader.GetRadioBindingDisplayString()
                : "Space";
            slotLabel.text = IsFilled ? $"RADIO\n[{binding}]" : "RADIO\n—";
            slotLabel.color = IsFilled
                ? Color.white
                : new Color(1f, 1f, 1f, 0.45f);
        }
    }

    private void BuildHud()
    {
        if (hudParent == null || slotBackground != null) return;

        GameObject slotObject = new(
            "RadioSlot",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        RectTransform slotRect = (RectTransform)slotObject.transform;
        slotRect.SetParent(hudParent, false);
        slotRect.anchorMin = Vector2.zero;
        slotRect.anchorMax = Vector2.zero;
        slotRect.pivot = Vector2.zero;
        slotRect.anchoredPosition = hudPosition;
        slotRect.sizeDelta = hudSize;
        slotBackground = slotObject.GetComponent<Image>();
        slotBackground.raycastTarget = false;

        GameObject labelObject = new(
            "Label",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        RectTransform labelRect = (RectTransform)labelObject.transform;
        labelRect.SetParent(slotRect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(8f, 8f);
        labelRect.offsetMax = new Vector2(-8f, -8f);

        slotLabel = labelObject.GetComponent<TextMeshProUGUI>();
        slotLabel.alignment = TextAlignmentOptions.Center;
        slotLabel.fontSize = 20f;
        slotLabel.raycastTarget = false;
    }
}
