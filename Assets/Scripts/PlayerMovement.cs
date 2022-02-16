using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    Rigidbody2D rb2D;
    [SerializeField] float moveSpeed;

    Vector2 movement;
    Animator animator;

    private void Start()
    {
        animator = GetComponent<Animator>();
        rb2D = GetComponent<Rigidbody2D>();
    }

    private void Update()
    {
        movement.x = Input.GetAxisRaw("Horizontal");
        movement.y = Input.GetAxisRaw("Vertical");

        transform.position = new Vector2(Mathf.Clamp(transform.position.x, -30f, 30f), Mathf.Clamp(transform.position.y, -30f, 30f));

        animator.SetFloat("Horizontal", movement.x);
        animator.SetFloat("Vertical", movement.y);
        animator.SetFloat("Speed", movement.sqrMagnitude);
    }

    void FixedUpdate()
    {
        rb2D.MovePosition(rb2D.position + movement * moveSpeed *Time.fixedDeltaTime);
    }
}