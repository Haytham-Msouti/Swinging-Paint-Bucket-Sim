using UnityEngine;

public class scriptForb : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created

    [SerializeField] Transform center;
    [SerializeField] Transform upCenter;
    [SerializeField] Transform side;
    [SerializeField] Transform ball;
    [SerializeField] Transform ropeAxis1;
    [SerializeField] Transform ropeAxis2;

    [SerializeField] float length = 0.3f;
    [SerializeField] float dragCoeff = 0.1f;
    [SerializeField] float dt = 0.02f;
    [SerializeField] float mass = 10f;
    [SerializeField] float g = 9.81f;

    Vector3 axis;
    Vector3 referenceDir;

    Vector3 angularVel ;
    Vector3 angularAcc ;
    Vector3 angularPos ;
    Vector3 forces ;

    void Start()
    {
        axis = upCenter.position - center.position;
        referenceDir = side.position - center.position;
    }

    void Update()
    //{
    //    FixedUpdate();
    //    //UpdatePhysics();
    //    //Debug.Log(angularPos);
    //    //Debug.Log(GetPhi());
    //    //Debug.Log(GetEpsilon());

    //    ////ropeAxis1.rotation = Quaternion.Euler(new Vector3(angularPos.x, 0, 0));
    //    ////ropeAxis2.localRotation = Quaternion.Euler(new Vector3(0, 0, angularPos.z));
    //    ////Vector3 dir = ball.position - ropeAxis1.position;
    //    ////ropeAxis1.rotation = Quaternion.LookRotation(dir);
    //    //ropeAxis1.LookAt(ball.position);
    //    //ropeAxis2.LookAt(ball.position);
    //}
    //void FixedUpdate()
    {
        UpdatePhysics();
        ropeAxis1.rotation = Quaternion.Euler(new Vector3(angularPos.x, 0, 0));
        ropeAxis2.localRotation = Quaternion.Euler(new Vector3(0, 0, angularPos.z));

        //Vector3 ropeDir = ball.position - ropeAxis1.position;
        //ropeAxis1.rotation = Quaternion.LookRotation(ropeDir);

        //Vector3 ropeDir2 = ball.position - ropeAxis2.position;
        //ropeAxis2.rotation = Quaternion.LookRotation(ropeDir2);
    }
    void UpdatePhysics()
    {
        float epsilon = GetEpsilon() * Mathf.Deg2Rad;
        float phi = GetPhi() * Mathf.Deg2Rad;

        float gravity = -mass * g * Mathf.Sin(phi);

        Vector3 movingForce = new (
            gravity * Mathf.Cos(epsilon),
            0,
            gravity * -Mathf.Sin(epsilon)
        );

        Vector3 dragForce = -dragCoeff * angularVel;

        forces = movingForce + dragForce;

        angularAcc = forces / (mass * length);
        angularVel += angularAcc * dt;
        angularPos += angularVel * dt;
    }

    float GetPhi()
    {
        Vector3 ropeDir = ball.position - upCenter.position;
        return Vector3.Angle(axis, ropeDir);
    }

    float GetEpsilon()
    {
        Vector3 currentDir = ball.position - upCenter.position;
        //Debug.Log(angularPos);
        float angle = Vector3.SignedAngle(
            Vector3.ProjectOnPlane(referenceDir, axis).normalized,
            Vector3.ProjectOnPlane(currentDir, axis).normalized,
            axis
        );

        return angle < 0 ? angle + 360f:angle ;
    }
}
