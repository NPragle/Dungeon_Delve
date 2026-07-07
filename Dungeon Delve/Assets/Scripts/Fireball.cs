using UnityEngine;

public class Fireball : MonoBehaviour
{
    [Header("Fly and explode settings")]
    [Tooltip("How long the fireball will exist before auto‑exploding (seconds).")]
    public float lifetime = 2f;

    [Tooltip("Optional explosion prefab that will be spawned at the impact location.")]
    public GameObject explosionPrefab;

    private float _timer;

    void Start()
    {
        _timer = lifetime;
    }

    void Update()
    {
        // count down until automatic explosion
        _timer -= Time.deltaTime;
        if (_timer <= 0f)
        {
            Explode();
        }
    }

    void OnCollisionEnter(Collision other)
    {
        // explode immediately when we hit anything (could filter by tag/layer)
        Explode();
    }

    private void Explode()
    {
        if (explosionPrefab != null)
        {
            Instantiate(explosionPrefab, transform.position, Quaternion.identity);
        }
        Destroy(gameObject);
    }
}
