// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "dong-mcu",
    platforms: [.macOS(.v13)],
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
