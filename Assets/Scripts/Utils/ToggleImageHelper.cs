using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Toggle))]
public class ToggleImageHelper : MonoBehaviour
{
    public Image targetImage;
    public Sprite onSprite;   // 拖入【开】状态的透明图
    public Sprite offSprite;  // 拖入【关】状态的透明图

    private Toggle toggle;

    private void Awake()
    {
        toggle = GetComponent<Toggle>();
        toggle.onValueChanged.AddListener(UpdateState);
        UpdateState(toggle.isOn);
    }

    private void UpdateState(bool isOn)
    {
        if (targetImage != null)
        {
            targetImage.sprite = isOn ? onSprite : offSprite;
        }
    }
}