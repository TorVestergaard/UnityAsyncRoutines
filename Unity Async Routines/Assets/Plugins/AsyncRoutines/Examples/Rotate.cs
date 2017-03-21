using UnityEngine;

namespace examples
{
    public class Rotate : MonoBehaviour
    {
        public float speed = 500f;

        void Update()
        {
            transform.Rotate(new Vector3(1,1,0), speed * Time.deltaTime);
        }
    }
}