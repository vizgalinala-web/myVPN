// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "MyVPNApi",
    platforms: [
        .iOS(.v16),
        .macOS(.v13)
    ],
    products: [
        .library(name: "MyVPNApi", targets: ["MyVPNApi"])
    ],
    targets: [
        .target(
            name: "MyVPNApi",
            path: "Sources/MyVPNApi"
        )
    ]
)
