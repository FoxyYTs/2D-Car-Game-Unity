using Assets.Logic.CarLocation;
using UnityEngine;

public class CarWritter : MonoBehaviour
{
    public string Id;
    public string Name;

    public CarLocation Location;

    [SerializeField] public AudioSource engineSound;

    public Sprite ExplosionSprite;

    [Tooltip("Advance the simulation in FixedUpdate with a constant step (AI training), instead of once per frame.")]
    public bool FixedStep = false;

    private CarWritter car;

    private readonly float WHEEL_BASE_IN_METERS = 1F;
    private const float METER_TO_PIXEL = 0.7F;
    // The map uses order 0 at the same depth; without this the car may be drawn behind it.
    private const int SORTING_ORDER_ABOVE_MAP = 1;

    void Awake()
    {
        Location = new(WHEEL_BASE_IN_METERS, LocationFrom(transform.position, transform.rotation), new KeyboardInputSource(Id));

        car = GetComponent<CarWritter>();
        GetComponent<SpriteRenderer>().sortingOrder = SORTING_ORDER_ABOVE_MAP;
        if (engineSound != null)
            engineSound.Play();
    }

    void Update()
    {
        if (!FixedStep)
            Step(Time.deltaTime);

        if (engineSound == null)
            return;

        if (Name == "blue")
            engineSound.panStereo = 1.0f;
        if (Name == "green")
            engineSound.panStereo = -1.0f;

        engineSound.pitch = car.Location.PowerTrain.RPM / 3000;
    }

    void FixedUpdate()
    {
        if (FixedStep)
            Step(Time.fixedDeltaTime);
    }

    private void Step(float deltaTime)
    {
        var next = Location.NextPosition(deltaTime);

        if (next is not null)
        {
            transform.position = PositionFrom(next);
            transform.rotation = RotationFrom(next);
        }
    }

    // Puts the car back at rest on the given pose, keeping its input source.
    public void ResetTo(Vector3 position, Quaternion rotation)
    {
        transform.SetPositionAndRotation(position, rotation);
        Location = new(WHEEL_BASE_IN_METERS, LocationFrom(position, rotation), Location.InputSource);
    }

    public void Explode()
    {
        var collision = GetComponent<CarCollisionReader>();

        if (collision.Collisions.By || collision.Collisions.OntoStatic)
        {
            GetComponent<SpriteRenderer>().sprite = ExplosionSprite;

            if (engineSound != null)
                engineSound.Stop();
        }
    }

    private Location LocationFrom(Vector3 position, Quaternion rotation)
    {
        return new(position.x / METER_TO_PIXEL, position.y / METER_TO_PIXEL, rotation.eulerAngles.z + 90);
    }

    private Quaternion RotationFrom(Location next)
    {
        return Quaternion.Euler(0, 0, -90 + next.DirectionInDegrees);
    }

    private Vector3 PositionFrom(Location next)
    {
        return new(next.Point.X * METER_TO_PIXEL, next.Point.Y * METER_TO_PIXEL);
    }
}
