using UnityEngine;

public class CloseBtn : BaseButton {
    protected override void OnClick() {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
