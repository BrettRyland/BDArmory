using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BDArmory.Utils
{
    public class DecoupledBooster : MonoBehaviour
    {
        Rigidbody rb;

        IEnumerator SelfDestructRoutine()
        {
            IEnumerator<Collider> col = gameObject.GetComponentsInChildren<Collider>().Cast<Collider>().GetEnumerator();
            while (col.MoveNext())
            {
                if (col.Current == null) continue;
                col.Current.enabled = false;
            }
            col.Dispose();
            yield return new WaitForSecondsFixed(5);
            Destroy(gameObject);
        }

        public void DecoupleBooster(in Vector3 startVelocity, float ejectSpeed)
        {
            transform.parent = null;

            rb = gameObject.AddComponent<Rigidbody>();
            gameObject.AddComponent<KSPForceApplier>();
            rb.velocity = startVelocity;
            rb.velocity += ejectSpeed * transform.forward;

            StartCoroutine(SelfDestructRoutine());
        }
    }
}
