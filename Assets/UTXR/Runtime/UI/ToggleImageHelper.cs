using UnityEngine;
using UnityEngine.UI;

namespace UTXR.UI
{
    [RequireComponent(typeof(Toggle))]
    public class ToggleImageHelper : MonoBehaviour
    {
        public Image targetImage;
        public Sprite onSprite;
        public Sprite offSprite;
    
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
}
