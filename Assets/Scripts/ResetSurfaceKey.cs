using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ResetSurfaceKey : MonoBehaviour
{
    [SerializeField] private AccumulationSurface surface;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R) && surface != null)
            surface.ResetSurface();
    }
}
