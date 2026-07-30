import AppKit

// GUI 없이 사용량 조회만 확인하는 진단 모드: dong-mcu --probe
if CommandLine.arguments.contains("--probe") {
    let done = DispatchSemaphore(value: 0)
    // main.swift 최상위 코드는 @MainActor라서 Task {}로 띄우면
    // done.wait()가 잡고 있는 메인 스레드를 기다리다 데드락 난다. 반드시 detached.
    Task.detached {
        do {
            let snapshot = try await UsageAPI.fetch()
            let plan = snapshot.planName ?? "(플랜 불명)"
            print("plan: \(plan)")
            if let fiveHour = snapshot.fiveHour {
                print("five_hour: \(fiveHour.utilization)% resets_at=\(fiveHour.resetsAt?.description ?? "-")")
            } else {
                print("five_hour: -")
            }
            if let sevenDay = snapshot.sevenDay {
                print("seven_day: \(sevenDay.utilization)% resets_at=\(sevenDay.resetsAt?.description ?? "-")")
            } else {
                print("seven_day: -")
            }
        } catch {
            print("error: \(error)")
        }
        done.signal()
    }
    done.wait()
    exit(0)
}

let application = NSApplication.shared
let delegate = AppDelegate()
application.delegate = delegate
application.run()
