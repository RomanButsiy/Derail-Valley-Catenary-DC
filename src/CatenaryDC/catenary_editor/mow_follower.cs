// Distributed under terms and conditions of CC0 licence. See LICENSE_CC0.txt for details.

#if DEBUG

using System;
using UnityEngine;

using DV.ThingTypes;

using CatenaryDC.catenary;

namespace CatenaryDC.catenary_editor;

internal class mow_follower: IDisposable
{
    private readonly distance_monitor _monitor;
    private bool _disposed;
    private readonly TrainCar           _mow_vehicle;

    public mow_follower(overhead_equipment system, TrainCar mow_vehicle)
    {
        _mow_vehicle = mow_vehicle;
        mow_vehicle.SimController.SimulationFlow.TickEvent += track_movement;
        _monitor = mow_vehicle.gameObject.AddComponent<distance_monitor>();
        editor.use_DM1U    = mow_vehicle.carType == TrainCarType.LocoDM1U;
        editor.mow_monitor = mow_vehicle.gameObject;
    }

    private void track_movement()
    {
        Transform front_location    = _mow_vehicle.FrontCouplerAnchor;
        Vector3   relative_position = front_location.position;
        editor.process_location(relative_position, front_location.forward);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_monitor != null) GameObject.Destroy(_monitor);
        _mow_vehicle.SimController.SimulationFlow.TickEvent -= track_movement;
        editor.use_DM1U    = false;
        editor.mow_monitor = null;
    }
}

public class distance_monitor: MonoBehaviour 
{
    public bool show     { get; set; }
    public int  distance { get; set; }
    
    void OnGUI()
    {
        if (show)
            GUI.Box(new Rect(10, 10, 200, 30), $"{distance} m left");
    }
}

#endif