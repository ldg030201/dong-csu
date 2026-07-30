// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "dong-mcu",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "dong-mcu", targets: ["DongMCU"])
    ],
    targets: [
        .executableTarget(
            name: "DongMCU",
            path: "Sources/DongMCU"
        )
    ]
)
