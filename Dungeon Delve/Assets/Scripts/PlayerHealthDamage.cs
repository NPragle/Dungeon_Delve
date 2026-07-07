using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerHealthDamage : MonoBehaviour
{
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
        // if (health <= 0)
        // {
        //     Debug.Log("Player has died!");
        //     // Add death handling logic here (e.g., respawn, game over screen)
        //     Destroy(gameObject);
        // }
    }
}
