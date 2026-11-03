import SwiftUI

/// 뷰가 들고 있는 상태. **SwiftUI 의 `@State` 를 직접 쓰지 않고 이걸 쓴다.**
///
/// macOS 27 SDK 부터 `@State` 는 매크로로 풀리는데, 그 매크로(`SwiftUIMacros`)는 Xcode 에만
/// 들어 있고 Command Line Tools 에는 없다. `@State` 를 쓰면 Xcode 없이 빌드하는 쪽 —
/// Homebrew 로 소스에서 까는 사용자까지 — 이 `StateMacro could not be found` 로 막힌다.
///
/// 이름이 다르면 매크로가 아니라 예전 그대로의 프로퍼티 래퍼로 풀린다. 동작은 같다.
typealias ViewState<Value> = SwiftUI.State<Value>
