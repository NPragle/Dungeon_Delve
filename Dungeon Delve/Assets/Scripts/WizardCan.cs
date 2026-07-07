using UnityEngine;

public class WizardCan : MonoBehaviour
{
    public int damage = 10;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        // keep Update empty; collision handled by the callbacks below
    }

        void OnCollisionEnter(Collision collision)
        {
        if (collision.gameObject.CompareTag("Enemy"))
        {
            collision.gameObject.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);
            Destroy(gameObject);
        }
        }

        void OnTriggerEnter(Collider other)
        {
        if (other.gameObject.CompareTag("Enemy"))
        {
            other.gameObject.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);
            Destroy(gameObject);
        }
    }
}

