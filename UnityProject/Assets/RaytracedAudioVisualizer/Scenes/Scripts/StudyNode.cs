using System;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(BoxCollider))] // Ensure a BoxCollider is always present
public class StudyNode : MonoBehaviour
{
    [Header("Node Settings")]
    public string nodeID;
    public bool isDecisionNode;

    private void Awake()
    {
        // Rigidbody setup
        Rigidbody rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        // Collider setup
        BoxCollider col = GetComponent<BoxCollider>();
        col.isTrigger = true;
        // Optional: Ensure the trigger is large enough
        if (col.size == Vector3.one) col.size = new Vector3(8, 3, 8); 
    }

    private void OnTriggerEnter(Collider other)
    {
        // Check both root and children for the tag
        if (other.CompareTag("Player") || other.transform.root.CompareTag("Player"))
        {
            Debug.Log($"Player entered node: {nodeID}");
            StudyGameManager.Instance.LogNodeEnter(this);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player") || other.transform.root.CompareTag("Player"))
        {
            StudyGameManager.Instance.LogNodeExit(this);
        }
    }
}