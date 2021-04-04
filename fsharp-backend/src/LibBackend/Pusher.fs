module LibBackend.Pusher

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open Tablecloth

let mutable initialized = false

let pusherClient : Lazy<PusherServer.Pusher> =
  lazy
    ((fun () ->
      printfn "Configuring rollbar"
      let options = PusherServer.PusherOptions()
      options.Cluster <- Config.pusherCluster

      let client =
        PusherServer.Pusher(
          Config.pusherID,
          Config.pusherKey,
          Config.pusherSecret,
          options
        )

      initialized <- true
      client)
       ()) // this awkward pattern is to prevent fantomas from breaking the code

// Send an event to pusher. Note: this is fired in the backgroup, and does not
// take any time from the current thread. You cannot wait for it, by design.
let push (canvasID : CanvasID) (eventName : string) (payload : string) : unit =
  let client = Lazy.force pusherClient
  assert initialized

  let (_ : Task<unit>) =
    task {
      try
        printfn $"Sending push to Pusher {eventName}: {canvasID}"

        let! (_ : PusherServer.ITriggerResult) =
          client.TriggerAsync(toString canvasID, eventName, payload)

        return ()
      with e ->
        // swallow this error
        printfn
          $"Error Sending push to Pusher {eventName}: {canvasID}: {e.ToString()}"

        LibService.Rollbar.send e

      return ()
    }
  // do not wait for the push task to finish, just fire and forget
  ()



// let push_new_trace_id
//     ~(execution_id : Types.id)
//     ~(canvas_id : Uuidm.t)
//     (trace_id : Uuidm.t)
//     (tlids : Types.tlid list) =
//   let payload = Analysis.to_new_trace_frontend (trace_id, tlids) in
//   push ~execution_id ~canvas_id ~event:"new_trace" payload
//
//
// let push_new_404
//     ~(execution_id : Types.id)
//     ~(canvas_id : Uuidm.t)
//     (fof : Stored_event.four_oh_four) =
//   let payload = Analysis.to_new_404_frontend fof in
//   push ~execution_id ~canvas_id ~event:"new_404" payload
//
//
// let push_new_static_deploy
//     ~(execution_id : Types.id)
//     ~(canvas_id : Uuidm.t)
//     (asset : Static_assets.static_deploy) =
//   let payload = Analysis.to_new_static_deploy_frontend asset in
//   push ~execution_id ~canvas_id ~event:"new_static_deploy" payload
//
//
// (* For exposure as a DarkInternal function *)
// let push_new_event
//     ~(execution_id : Types.id)
//     ~(canvas_id : Uuidm.t)
//     ~(event : string)
//     (payload : string) =
//   push ~execution_id ~canvas_id ~event payload

let pushWorkerStates (canvasID : CanvasID) (ws : EventQueue.WorkerStates.T) : unit =
  let payload = Json.OCamlCompatible.serialize ws
  push canvasID "worker_state" payload

type JsConfig = { enabled : bool; key : string; cluster : string }

let jsConfigString =
  let p = { enabled = true; key = Config.pusherKey; cluster = Config.pusherCluster }
  Json.Vanilla.serialize p
