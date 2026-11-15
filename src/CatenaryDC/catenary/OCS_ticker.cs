// Distributed under terms and conditions of CC0 licence. See LICENSE_CC0.txt for details.

using UnityEngine;
using CatenaryDC.Api;

namespace CatenaryDC.catenary;

public partial class overhead_equipment
{
    private class OCS_ticker: MonoBehaviour
    {
        private bool _track_player_movement = false;
        private overhead_equipment? _owner;

        public void bind_owner(overhead_equipment owner) => _owner = owner;

        private bool can_tick(overhead_equipment? owner) =>
            owner != null && ReferenceEquals(owner, _owner) &&
            ReferenceEquals(owner, _system) && owner._ready;
        
        void Update()
        {
            overhead_equipment? owner = _owner;
            if (!can_tick(owner))
                return;
            owner!.authoring_tick();
            if (_track_player_movement && can_tick(owner))
            {
                // Unity can destroy the native camera before PlayerManager clears
                // its managed reference during reload. ?. and "is null" bypass
                // UnityEngine.Object's lifetime check; never use them here.
                var camera = PlayerManager.ActiveCamera;
                Transform? player_view = camera != null ? camera.transform : null;
                if (player_view == null)
                {
                    player_view = PlayerManager.PlayerTransform;
                    if (player_view == null)
                        return;
                }
                owner.handle_scenery_visibility(player_view.position);
            }
        }

        void FixedUpdate()
        {
            overhead_equipment? owner = _owner;
            if (can_tick(owner))
            {
                CatenaryApi.RaiseSimulationStep();
                // A subscriber may end the world. Do not tick disposed state or
                // the replacement world from this old component.
                if (can_tick(owner)) owner!.simulate_all_substations_load();
            }
        }

        public void suspend_tracker()
        {
            _track_player_movement = false;
        }

        public void resume_tracker()
        {
            _track_player_movement = true;
        }

        public void stop_tracker()
        {
            _track_player_movement = false;
            _owner = null;
            if (this != null) enabled = false;
        }
    }
}
