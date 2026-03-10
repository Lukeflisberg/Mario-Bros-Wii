using UnityEngine;

public class player : MonoBehaviour
{
    public float moveSpeed = 10f;
    public float jumpForce = 10f;
    [SerializeField] private bool isGrounded = false;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {

    }

    private void OnTriggerEnter2D(Collider2D other) {
        
    }
}
