use std::{
    collections::HashMap,
    io::{BufRead, BufReader, Write},
    path::{Path, PathBuf},
    process::{Child, ChildStderr, ChildStdin, ChildStdout, Command, Stdio},
    sync::{Arc, Mutex},
};

use serde::{Deserialize, Serialize};
use serde_json::Value;
use tauri::{AppHandle, Emitter, Manager, State};
use tokio::sync::oneshot;

const AGENTHOST_EVENT: &str = "agenthost://event";
const AGENTHOST_STATE_EVENT: &str = "agenthost://state";

#[derive(Default)]
struct AgentHostState {
    inner: Mutex<AgentHostManager>,
}

#[derive(Default)]
struct AgentHostManager {
    process: Option<AgentHostProcess>,
    pending: Arc<Mutex<HashMap<String, oneshot::Sender<AgentHostResponseEnvelope>>>>,
    next_request_id: u64,
}

struct AgentHostProcess {
    child: Child,
    stdin: Arc<Mutex<ChildStdin>>,
}

#[derive(Debug, Deserialize, Serialize, Clone)]
#[serde(rename_all = "camelCase")]
struct AgentHostRequestEnvelope {
    request_id: String,
    command: String,
    payload: Value,
}

#[derive(Debug, Deserialize, Serialize, Clone)]
#[serde(rename_all = "camelCase")]
struct AgentHostResponseEnvelope {
    request_id: String,
    command: String,
    success: bool,
    timestamp: String,
    payload: Option<Value>,
    error: Option<AgentHostErrorPayload>,
}

#[derive(Debug, Deserialize, Serialize, Clone)]
#[serde(rename_all = "camelCase")]
struct AgentHostErrorPayload {
    code: String,
    message: String,
    details: Option<String>,
}

#[derive(Debug, Deserialize, Serialize, Clone)]
#[serde(rename_all = "camelCase")]
struct AgentHostEventEnvelope {
    event: String,
    timestamp: String,
    payload: Value,
}

#[derive(Debug, Deserialize, Serialize, Clone)]
#[serde(rename_all = "camelCase")]
struct AgentHostStatePayload {
    status: String,
    detail: Option<String>,
}

#[tauri::command]
async fn agent_host_request(
    app: AppHandle,
    state: State<'_, AgentHostState>,
    command: String,
    payload: Value,
) -> Result<Value, String> {
    let response = state.send_request(&app, command, payload).await?;
    if response.success {
        Ok(response.payload.unwrap_or(Value::Null))
    } else {
        let error = response.error.unwrap_or(AgentHostErrorPayload {
            code: "unknown_error".to_string(),
            message: "AgentHost request failed.".to_string(),
            details: None,
        });
        Err(match error.details {
            Some(details) if !details.is_empty() => format!("{}: {}", error.message, details),
            _ => error.message,
        })
    }
}

impl AgentHostState {
    async fn send_request(
        &self,
        app: &AppHandle,
        command: String,
        payload: Value,
    ) -> Result<AgentHostResponseEnvelope, String> {
        let (request_id, stdin, pending) = {
            let mut manager = self.inner.lock().map_err(|_| "AgentHost lock poisoned.".to_string())?;
            manager.ensure_started(app)?;
            manager.next_request_id += 1;
            let request_id = format!("desktop-{}", manager.next_request_id);
            let stdin = manager
                .process
                .as_ref()
                .map(|process| process.stdin.clone())
                .ok_or_else(|| "AgentHost stdin is unavailable.".to_string())?;
            (request_id, stdin, manager.pending.clone())
        };

        let (sender, receiver) = oneshot::channel();
        pending
            .lock()
            .map_err(|_| "AgentHost pending map lock poisoned.".to_string())?
            .insert(request_id.clone(), sender);

        let envelope = AgentHostRequestEnvelope {
            request_id: request_id.clone(),
            command,
            payload,
        };

        let line = serde_json::to_string(&envelope).map_err(|error| error.to_string())?;
        {
            let mut writer = stdin
                .lock()
                .map_err(|_| "AgentHost stdin lock poisoned.".to_string())?;
            writer
                .write_all(line.as_bytes())
                .and_then(|_| writer.write_all(b"\n"))
                .and_then(|_| writer.flush())
                .map_err(|error| error.to_string())?;
        }

        receiver.await.map_err(|_| "AgentHost request channel closed.".to_string())
    }
}

impl AgentHostManager {
    fn ensure_started(&mut self, app: &AppHandle) -> Result<(), String> {
        if let Some(process) = &mut self.process {
            match process.child.try_wait() {
                Ok(None) => return Ok(()),
                Ok(Some(status)) => {
                    self.process = None;
                    let _ = app.emit(
                        AGENTHOST_STATE_EVENT,
                        AgentHostStatePayload {
                            status: "stopped".to_string(),
                            detail: Some(format!("AgentHost exited with status {status}.")),
                        },
                    );
                }
                Err(error) => {
                    self.process = None;
                    let _ = app.emit(
                        AGENTHOST_STATE_EVENT,
                        AgentHostStatePayload {
                            status: "error".to_string(),
                            detail: Some(format!("Failed to inspect AgentHost process: {error}")),
                        },
                    );
                }
            }
        }

        let (program, arguments, working_directory) = resolve_agent_host_launch(app)?;
        let mut command = Command::new(&program);
        command
            .args(&arguments)
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .current_dir(working_directory);

        let mut child = command.spawn().map_err(|error| {
            format!("Failed to start AgentHost using '{}': {}", program.to_string_lossy(), error)
        })?;

        let stdin = child
            .stdin
            .take()
            .ok_or_else(|| "Failed to capture AgentHost stdin.".to_string())?;
        let stdout = child
            .stdout
            .take()
            .ok_or_else(|| "Failed to capture AgentHost stdout.".to_string())?;
        let stderr = child
            .stderr
            .take()
            .ok_or_else(|| "Failed to capture AgentHost stderr.".to_string())?;

        let pending = self.pending.clone();
        spawn_stdout_reader(app.clone(), stdout, pending.clone());
        spawn_stderr_reader(app.clone(), stderr, pending);

        self.process = Some(AgentHostProcess {
            child,
            stdin: Arc::new(Mutex::new(stdin)),
        });

        let _ = app.emit(
            AGENTHOST_STATE_EVENT,
            AgentHostStatePayload {
                status: "started".to_string(),
                detail: None,
            },
        );
        Ok(())
    }
}

fn spawn_stdout_reader(
    app: AppHandle,
    stdout: ChildStdout,
    pending: Arc<Mutex<HashMap<String, oneshot::Sender<AgentHostResponseEnvelope>>>>,
) {
    std::thread::spawn(move || {
        let reader = BufReader::new(stdout);
        for line_result in reader.lines() {
            let line = match line_result {
                Ok(line) if !line.trim().is_empty() => line,
                Ok(_) => continue,
                Err(error) => {
                    let _ = app.emit(
                        AGENTHOST_STATE_EVENT,
                        AgentHostStatePayload {
                            status: "error".to_string(),
                            detail: Some(format!("AgentHost stdout read failed: {error}")),
                        },
                    );
                    break;
                }
            };

            let value: Value = match serde_json::from_str(&line) {
                Ok(value) => value,
                Err(error) => {
                    let _ = app.emit(
                        AGENTHOST_STATE_EVENT,
                        AgentHostStatePayload {
                            status: "error".to_string(),
                            detail: Some(format!("AgentHost emitted invalid JSON: {error}")),
                        },
                    );
                    continue;
                }
            };

            if value.get("event").is_some() {
                if let Ok(agent_event) = serde_json::from_value::<AgentHostEventEnvelope>(value.clone()) {
                    let _ = app.emit(AGENTHOST_EVENT, agent_event);
                }
                continue;
            }

            if value.get("requestId").is_some() {
                if let Ok(response) = serde_json::from_value::<AgentHostResponseEnvelope>(value) {
                    if let Ok(mut pending_requests) = pending.lock() {
                        if let Some(sender) = pending_requests.remove(&response.request_id) {
                            let _ = sender.send(response);
                        }
                    }
                }
            }
        }

        if let Ok(mut pending_requests) = pending.lock() {
            for (_, sender) in pending_requests.drain() {
                let _ = sender.send(AgentHostResponseEnvelope {
                    request_id: String::new(),
                    command: "unknown".to_string(),
                    success: false,
                    timestamp: String::new(),
                    payload: None,
                    error: Some(AgentHostErrorPayload {
                        code: "host_stopped".to_string(),
                        message: "AgentHost stopped.".to_string(),
                        details: None,
                    }),
                });
            }
        }

        let _ = app.emit(
            AGENTHOST_STATE_EVENT,
            AgentHostStatePayload {
                status: "stopped".to_string(),
                detail: None,
            },
        );
    });
}

fn spawn_stderr_reader(
    app: AppHandle,
    stderr: ChildStderr,
    _pending: Arc<Mutex<HashMap<String, oneshot::Sender<AgentHostResponseEnvelope>>>>,
) {
    std::thread::spawn(move || {
        let reader = BufReader::new(stderr);
        for line in reader.lines().map_while(Result::ok) {
            if line.trim().is_empty() {
                continue;
            }

            let _ = app.emit(
                AGENTHOST_STATE_EVENT,
                AgentHostStatePayload {
                    status: "stderr".to_string(),
                    detail: Some(line),
                },
            );
        }
    });
}

fn resolve_agent_host_launch(app: &AppHandle) -> Result<(PathBuf, Vec<String>, PathBuf), String> {
    if cfg!(debug_assertions) {
        let repo_root = Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../..")
            .canonicalize()
            .map_err(|error| format!("Failed to resolve repo root: {error}"))?;
        let project_path = repo_root.join("src/ClawSharp.AgentHost/ClawSharp.AgentHost.csproj");
        return Ok((
            PathBuf::from("dotnet"),
            vec![
                "run".to_string(),
                "--project".to_string(),
                project_path.to_string_lossy().into_owned(),
                "--".to_string(),
            ],
            repo_root,
        ));
    }

    let resource_dir = app
        .path()
        .resource_dir()
        .map_err(|error| format!("Failed to resolve resource directory: {error}"))?;
    let sidecar_dir = resource_dir.join("binaries").join(current_rid_folder());
    let executable_name = if cfg!(target_os = "windows") {
        "clawsharp-agenthost.exe"
    } else {
        "clawsharp-agenthost"
    };
    let executable_path = sidecar_dir.join(executable_name);
    if !executable_path.exists() {
        return Err(format!(
            "AgentHost sidecar not found at {}",
            executable_path.to_string_lossy()
        ));
    }

    Ok((executable_path, Vec::new(), resource_dir))
}

fn current_rid_folder() -> &'static str {
    if cfg!(all(target_os = "windows", target_arch = "x86_64")) {
        "win-x64"
    } else if cfg!(all(target_os = "windows", target_arch = "aarch64")) {
        "win-arm64"
    } else if cfg!(all(target_os = "linux", target_arch = "x86_64")) {
        "linux-x64"
    } else if cfg!(all(target_os = "linux", target_arch = "aarch64")) {
        "linux-arm64"
    } else if cfg!(all(target_os = "macos", target_arch = "x86_64")) {
        "osx-x64"
    } else {
        "osx-arm64"
    }
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .manage(AgentHostState::default())
        .invoke_handler(tauri::generate_handler![agent_host_request])
        .run(tauri::generate_context!())
        .expect("error while running ClawSharp desktop");
}

fn main() {
    run();
}
