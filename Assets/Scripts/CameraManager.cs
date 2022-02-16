using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraManager : MonoBehaviour
{
    public Transform target;
    public float offsetY = 3;

    public float smoothX, smoothY;

    private void LateUpdate()
    {
        transform.position = new Vector3
           (Mathf.Lerp(transform.position.x, target.transform.position.x, smoothX * Time.deltaTime), Mathf.Lerp(transform.position.y, target.transform.position.y, smoothY * Time.deltaTime), transform.position.z);
    }
}
