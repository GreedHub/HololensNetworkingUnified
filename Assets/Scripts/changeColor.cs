using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using HoloToolkit.Unity.InputModule;

public class changeColor : MonoBehaviour, IInputClickHandler
{

    public void OnInputClicked(InputClickedEventData eventData)
    {
        gameObject.GetComponent<Renderer>().material.color = new Color(Random.Range(0, 1f), Random.Range(0, 1f), Random.Range(0, 1f)); 
    }

}
