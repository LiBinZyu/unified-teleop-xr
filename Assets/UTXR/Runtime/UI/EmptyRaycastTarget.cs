using UnityEngine;
using UnityEngine.UI;

namespace UTXR.UI
{
    [RequireComponent(typeof(CanvasRenderer))]
    public class EmptyRaycastTarget : Graphic
    {
        public override void SetMaterialDirty() { }
        public override void SetVerticesDirty() { }
    
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
        }
    }
}

