using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public float moveSpeed = 5f;
    public float walkSpeed = 5f;
    public float sprintSpeed = 10f;

    public int maxHealth = 10;
    public int health;

    // Start is called before the first frame update
    void Start()
    {
        health = maxHealth;
    }

    // Update is called once per frame
    void Update()
    {

        float moveX = Input.GetAxis("Horizontal");
        float moveY = Input.GetAxis("Vertical");
        Vector3 movement = new Vector3(moveX, 0, moveY) * moveSpeed * Time.deltaTime;
        transform.Translate(movement, Space.World);

        if (Input.GetKeyDown(KeyCode.Space))
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null && Mathf.Abs(rb.velocity.y) < 0.01f)
            {
                rb.AddForce(Vector3.up * 5f, ForceMode.Impulse);
            }
        }

        if (Input.GetKey(KeyCode.LeftShift))
        {
            moveSpeed = sprintSpeed;
        }
        else
        {
            moveSpeed = walkSpeed;
        }

    }
}
