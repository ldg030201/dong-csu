import Combine
import Darwin
import Foundation

/// 이 앱이 지금 쓰고 있는 자원.
struct ProcessUsage: Equatable {
    /// 직전 표본 이후의 평균 CPU 점유율(%).
    var cpuPercent: Double
    /// 실제 점유 메모리(phys_footprint). RSS는 공용 프레임워크까지 포함해서 과장된다.
    var footprintBytes: UInt64

    var footprintText: String {
        String(format: "%.0fMB", Double(footprintBytes) / 1_048_576)
    }

    var cpuText: String {
        String(format: "%.1f%%", cpuPercent)
    }
}

/// 자기 프로세스의 자원 사용량을 읽는다.
/// `ps` 같은 외부 프로세스를 띄우지 않고 커널에 직접 물어보므로 표본 자체가 거의 공짜다.
@MainActor
final class ProcessUsageSampler {
    private var previousCPUSeconds: Double?
    private var previousSampledAt: Date?

    func sample(now: Date = Date()) -> ProcessUsage {
        let cpuSeconds = Self.cpuSeconds()

        var percent = 0.0
        if let previousCPUSeconds, let previousSampledAt {
            let elapsed = now.timeIntervalSince(previousSampledAt)
            if elapsed > 0 {
                percent = max(0, (cpuSeconds - previousCPUSeconds) / elapsed * 100)
            }
        }
        previousCPUSeconds = cpuSeconds
        previousSampledAt = now

        return ProcessUsage(cpuPercent: percent, footprintBytes: Self.footprintBytes())
    }

    /// 프로세스가 시작한 뒤 지금까지 쓴 CPU 시간(초).
    private static func cpuSeconds() -> Double {
        var usage = rusage_info_current()
        let result = withUnsafeMutablePointer(to: &usage) { pointer in
            pointer.withMemoryRebound(to: rusage_info_t?.self, capacity: 1) {
                proc_pid_rusage(getpid(), RUSAGE_INFO_CURRENT, $0)
            }
        }
        guard result == 0 else { return 0 }
        return Double(usage.ri_user_time + usage.ri_system_time) / 1_000_000_000
    }

    /// `footprint` 명령이 보여주는 값과 같은 기준의 메모리.
    private static func footprintBytes() -> UInt64 {
        var info = task_vm_info_data_t()
        var count = mach_msg_type_number_t(
            MemoryLayout<task_vm_info_data_t>.size / MemoryLayout<natural_t>.size
        )
        let result = withUnsafeMutablePointer(to: &info) { pointer in
            pointer.withMemoryRebound(to: integer_t.self, capacity: Int(count)) {
                task_info(mach_task_self_, task_flavor_t(TASK_VM_INFO), $0, &count)
            }
        }
        return result == KERN_SUCCESS ? UInt64(info.phys_footprint) : 0
    }
}

/// 표시가 켜져 있을 때만 주기적으로 표본을 뜬다.
/// 뷰의 body에서 직접 재면 SwiftUI가 body를 여러 번 평가할 때 값이 튀므로 여기서 관리한다.
@MainActor
final class ProcessUsageMonitor: ObservableObject {
    @Published private(set) var usage = ProcessUsage(cpuPercent: 0, footprintBytes: 0)

    private let sampler = ProcessUsageSampler()
    private var timer: Timer?

    /// 2초면 눈으로 보기 충분하고, 표본 자체의 비용도 무시할 수준이다.
    private static let interval: TimeInterval = 2

    var isRunning: Bool { timer != nil }

    func start() {
        guard timer == nil else { return }
        usage = sampler.sample()

        let timer = Timer(timeInterval: Self.interval, repeats: true) { [weak self] _ in
            // 타이머를 메인 런루프에 걸었으므로 콜백도 메인 스레드에서 온다.
            MainActor.assumeIsolated {
                guard let self else { return }
                self.usage = self.sampler.sample()
            }
        }
        timer.tolerance = 0.5
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    func stop() {
        timer?.invalidate()
        timer = nil
    }
}
