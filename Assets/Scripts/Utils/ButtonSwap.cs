using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class ButtonSwap : MonoBehaviour
{
    public Button buttonFirst; 
    public Button buttonConfirm;

    public UnityEvent buttonConfirmEvent;
    
    [Tooltip("Fall back time (s) from Confirm Button to First Button")]
    public float fallbackTime = 3.0f;

    private Coroutine resetCoroutine;
    
    public void OnClickFirstButton()
    {
        buttonFirst.gameObject.SetActive(false);
        buttonConfirm.gameObject.SetActive(true);

        // 刷新倒计时
        if (resetCoroutine != null)
        {
            StopCoroutine(resetCoroutine);
        }
        resetCoroutine = StartCoroutine(WaitAndReset());
    }

    public void OnClickConfirmButton()
    {
        // 触发确认事件（例如退出应用）
        buttonConfirmEvent?.Invoke();
        
        // 重置回初始状态
        buttonFirst.gameObject.SetActive(true);
        buttonConfirm.gameObject.SetActive(false);
    }

    public void ToggleGameObjectActive(GameObject targetObj)
    {
        if (targetObj != null)
        {
            targetObj.SetActive(!targetObj.activeSelf);
        }
    }
    
    private IEnumerator WaitAndReset()
    {
        // 使用 Realtime 保证即使游戏暂停也能正常倒计时
        yield return new WaitForSecondsRealtime(fallbackTime);
        
        buttonFirst.gameObject.SetActive(true);
        buttonConfirm.gameObject.SetActive(false);
    }

    private void Awake()
    {
        buttonFirst.onClick.AddListener(OnClickFirstButton);
        buttonConfirm.onClick.AddListener(OnClickConfirmButton);
    }

    private void Start()
    {
        buttonFirst.gameObject.SetActive(true);
        buttonConfirm.gameObject.SetActive(false);
    }
    
    private void OnDestroy()
    {
        buttonFirst.onClick.RemoveListener(OnClickFirstButton);
        buttonConfirm.onClick.RemoveListener(OnClickConfirmButton);
    }
    private void OnEnabled()
    {
        buttonFirst.gameObject.SetActive(true);
        buttonConfirm.gameObject.SetActive(false);
    }
    
}