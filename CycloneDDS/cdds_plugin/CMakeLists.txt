cmake_minimum_required(VERSION 3.16)
project(cdds_plugin LANGUAGES C CXX)

set(CMAKE_C_STANDARD 99)
set(CMAKE_CXX_STANDARD 14)

if(NOT CycloneDDS_DIR)
    if(WIN32)
        set(CycloneDDS_DIR "D:/apex_teleop/km_apex_openxr/cyclonedds/install/lib/cmake/CycloneDDS")
    elseif(ANDROID)
        set(CycloneDDS_DIR "D:/apex_teleop/km_apex_openxr/cyclonedds/install_android_arm64/lib/cmake/CycloneDDS")
    endif()
endif()

find_package(CycloneDDS REQUIRED)

include_directories(
    include
    generated
)

file(GLOB_RECURSE GENERATED_SOURCES "generated/*.c")

add_library(cdds_plugin SHARED
    src/cdds_plugin.c
    ${GENERATED_SOURCES}
)

if(WIN32)
    target_link_libraries(cdds_plugin PRIVATE
        CycloneDDS::ddsc
        ws2_32
        iphlpapi
        bcrypt
    )

    add_executable(test_publish
        test/test_publish.c
    )
    target_link_libraries(test_publish PRIVATE
        cdds_plugin
    )
elseif(ANDROID)
    target_link_libraries(cdds_plugin PRIVATE
        CycloneDDS::ddsc
        log
    )
else()
    target_link_libraries(cdds_plugin PRIVATE
        CycloneDDS::ddsc
    )
endif()
