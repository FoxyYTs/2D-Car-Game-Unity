using Assets.Logic.Game;
using System;
using UnityEngine;

public class CarCollisionReader : MonoBehaviour
{
    public Collisions Collisions = new();
    private CarWritter carWritter;

    // Versus rules by default; assign another handler right after Instantiate (before Start) to override it.
    public Action<CarCollisionReader> CrashHandler { get; set; }

    void Awake()
    {
        carWritter = GetComponent<CarWritter>();
    }

    void Start()
    {
        if (CrashHandler is null)
        {
            CrashHandler = EndVersusGame;
            GameGoal.RegisterCollidable(carWritter.Name, Collisions);
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.collider.gameObject.tag == collision.otherCollider.gameObject.tag)
        {
            if (collision.collider.GetType() == typeof(BoxCollider2D) && collision.otherCollider.GetType() == typeof(CircleCollider2D))
                Collisions.OntoMoving = true;
            else if (collision.collider.GetType() == typeof(CircleCollider2D) && collision.otherCollider.GetType() == typeof(BoxCollider2D))
                Collisions.By = true;
            else
            {
                Collisions.By = true;
                Collisions.OntoMoving = true;
            }
        }
        else
        {
            Collisions.OntoStatic = true;
            Collisions.Obstacle = collision.collider.name;
        }

        CrashHandler(this);
    }

    private void EndVersusGame(CarCollisionReader car)
    {
        var gameController = GameObject.Find("Main Camera").gameObject.GetComponent<GameController>();

        GameGoal.EvaluateCollisions(Collisions, carWritter.Name);
        carWritter.Explode();
        gameController.ShowEndgame();
    }
}