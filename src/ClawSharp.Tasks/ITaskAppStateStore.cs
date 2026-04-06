// TS origin: ./state/AppState.ts, ./utils/task/framework.ts
namespace ClawSharp.Tasks;

public interface ITaskAppStateStore
{
    TaskAppState GetAppState();

    void SetAppState(Func<TaskAppState, TaskAppState> updater);
}
