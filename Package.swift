// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "dong-csu",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "dong-csu", targets: ["DongCSU"])
    ],
    targets: [
        .executableTarget(
            name: "DongCSU",
            path: "Sources/DongCSU"
        )
    ]
)
