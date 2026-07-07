using UnityEngine;

public class BallEnemyAI : MonoBehaviour
{
    public float MoveSpeed = 1f;
    public int MaxHealth = 100;
    private int CurrentHealth;
    public int ContactDamage = 0;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        CurrentHealth = MaxHealth;
    }

    // Update is called once per frame
    void Update()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) return;
        Vector3 direction = (player.transform.position - transform.position).normalized;
        transform.position += direction * MoveSpeed * Time.deltaTime;

        if (CurrentHealth <= 0)
        {
            Destroy(gameObject);
        }
    }

    void OnCollisionEnter(Collision collision){
        if (collision.gameObject.CompareTag("Player"))
        {
            PlayerHealthDamage playerHealth = collision.gameObject.GetComponent<PlayerHealthDamage>();
            if (playerHealth != null)
            {
                playerHealth.health -= ContactDamage;
                Debug.Log("Player took " + ContactDamage + " damage from Ball Enemy! Health: " + playerHealth.health);
            }
        }
    }
}
